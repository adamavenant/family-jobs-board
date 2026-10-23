import { useSyncExternalStore } from "react";
import type { ShouldRevalidateFunctionArgs } from "react-router";

import { AuthApiError } from "../api/auth";

// Every in-scope action waits out a household-wide grace period before it is
// sent to the API, so Undo never needs a server endpoint. Route actions queue
// the API call here and return at once; the queue owns the countdown, sends
// the request, and asks the router to revalidate. It is module state rather
// than React context because route actions (outside the React tree) queue
// into it, and queued work must outlive any one page.

export const defaultUndoGracePeriodSeconds = 30;
export const minimumUndoGracePeriodSeconds = 5;
export const maximumUndoGracePeriodSeconds = 120;
/** How long a saved action stays shown as saving while the page revalidates. */
const revalidationLimitMs = 15_000;

export interface QueuedAction {
  readonly id: string;
  /** What happened, for example "Approved “Feed the dog” for Sam". */
  readonly description: string;
  /** The submitted form, so pages can show the after-action state. */
  readonly formData: FormData;
  /** "waiting" while it can still be undone; "saving" once it was sent. */
  readonly status: "waiting" | "saving";
  readonly durationMs: number;
  /** Time left when the countdown last paused or resumed. */
  readonly remainingMs: number;
  /** When the countdown last resumed, or null while it is paused or saving. */
  readonly runningSince: number | null;
}

export interface ToastNotice {
  readonly id: string;
  readonly tone: "success" | "error";
  readonly message: string;
  /** A failed save that can be sent again exactly as it was queued. */
  readonly canRetry: boolean;
}

export interface UndoQueueSnapshot {
  readonly actions: readonly QueuedAction[];
  readonly notices: readonly ToastNotice[];
}

export interface UndoableAction {
  description: string;
  form: FormData;
  /** Sends the action to the API. Runs once, after the grace period. */
  commit: () => Promise<unknown>;
  /** Explains a failed commit, for example from the API's problem details. */
  failure: (error: unknown) => string;
}

interface Entry {
  action: QueuedAction;
  commit: () => Promise<unknown>;
  failure: (error: unknown) => string;
  timer?: ReturnType<typeof setTimeout>;
  /** Settles once the request has been answered or has failed. */
  committed?: Promise<void>;
  /** Settles once the page has also revalidated and the entry is gone. */
  saved?: Promise<void>;
}

export interface QueueConnection {
  /**
   * Reloads the page's data from the server, for example the router's. Calls
   * `shown` as the page renders the reloaded data, so the saved action leaves
   * the page in that same moment, and resolves once it has.
   */
  revalidate: (shown: () => void) => Promise<unknown> | void;
  /** Called with how long an action will wait, so the session can outlast it. */
  keepAlive?: (waitMs: number) => void;
}

export interface UndoQueue {
  enqueue(action: UndoableAction): string;
  undo(id: string): void;
  saveNow(id: string): void;
  pause(id: string): void;
  resume(id: string): void;
  /**
   * Sends every queued action now and resolves when each request has been
   * answered (without waiting for the page to revalidate).
   */
  flush(): Promise<void>;
  setGracePeriodSeconds(seconds: number | undefined): void;
  connect(connection: QueueConnection): () => void;
  notify(message: string): void;
  /** Sends a failed action again with the same request, then revalidates. */
  retry(noticeId: string): void;
  /**
   * Withdraws Try again from failed actions, so a different person signing in
   * can never resend someone else's action as their own.
   */
  forgetRetries(): void;
  dismissNotice(id: string): void;
  subscribe(listener: () => void): () => void;
  getSnapshot(): UndoQueueSnapshot;
  /** Drops everything without sending it. For tests. */
  reset(): void;
}

export function createUndoQueue(): UndoQueue {
  const entries = new Map<string, Entry>();
  // Failed saves by the id of the error notice that offers to try again.
  const failed = new Map<string, Entry>();
  const listeners = new Set<() => void>();
  let notices: ToastNotice[] = [];
  let snapshot: UndoQueueSnapshot = { actions: [], notices };
  let gracePeriodMs = defaultUndoGracePeriodSeconds * 1000;
  let connection: QueueConnection | undefined;
  let nextId = 0;

  function emit() {
    snapshot = {
      actions: [...entries.values()].map((entry) => entry.action),
      notices,
    };
    listeners.forEach((listener) => listener());
  }

  function update(entry: Entry, changes: Partial<QueuedAction>) {
    entry.action = { ...entry.action, ...changes };
  }

  function waiting(id: string): Entry | undefined {
    const entry = entries.get(id);
    return entry?.action.status === "waiting" ? entry : undefined;
  }

  function startTimer(entry: Entry) {
    clearTimeout(entry.timer);
    entry.timer = setTimeout(() => save(entry), entry.action.remainingMs);
    connection?.keepAlive?.(entry.action.remainingMs);
  }

  function save(entry: Entry): Promise<void> {
    if (entry.committed) {
      return entry.committed;
    }

    clearTimeout(entry.timer);
    update(entry, { status: "saving", runningSince: null });
    emit();
    entry.committed = (async () => {
      try {
        await entry.commit();
      } catch (error) {
        const noticeId = addNotice(
          "error",
          `Not saved: ${entry.action.description}. ${entry.failure(error)}`,
          true,
        );
        failed.set(noticeId, entry);
      }
    })();
    entry.saved = entry.committed.then(async () => {
      const leave = () => {
        if (entries.delete(entry.action.id)) {
          emit();
        }
      };
      try {
        // Show the server's state, whether the action was saved or refused.
        await withinLimit(connection?.revalidate(leave));
      } finally {
        leave();
      }
    });
    return entry.committed;
  }

  function addNotice(
    tone: ToastNotice["tone"],
    message: string,
    canRetry = false,
  ): string {
    nextId += 1;
    const id = `notice-${nextId}`;
    // One success message at a time, as before; every error stays until dismissed.
    notices = [
      ...notices.filter(
        (notice) => tone === "error" || notice.tone === "error",
      ),
      { id, tone, message, canRetry },
    ];
    return id;
  }

  /**
   * Waits for a revalidation, but never keeps an item locked as saving for
   * longer than the limit if the reload stalls.
   */
  async function withinLimit(work: Promise<unknown> | void): Promise<void> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      await Promise.race([
        Promise.resolve(work).catch(() => undefined),
        new Promise<void>((resolve) => {
          timer = setTimeout(resolve, revalidationLimitMs);
        }),
      ]);
    } finally {
      clearTimeout(timer);
    }
  }

  function removeNotice(id: string) {
    failed.delete(id);
    notices = notices.filter((notice) => notice.id !== id);
  }

  return {
    enqueue({ description, form, commit, failure }) {
      nextId += 1;
      const id = `undo-${nextId}`;
      const entry: Entry = {
        action: {
          id,
          description,
          formData: form,
          status: "waiting",
          durationMs: gracePeriodMs,
          remainingMs: gracePeriodMs,
          runningSince: Date.now(),
        },
        commit,
        failure,
      };
      entries.set(id, entry);
      startTimer(entry);
      emit();
      return id;
    },
    undo(id) {
      const entry = entries.get(id);
      if (!entry) {
        return;
      }
      if (entry.action.status !== "waiting") {
        // The countdown won the race; say so rather than look undone.
        addNotice(
          "error",
          `Too late to undo: ${entry.action.description}. It was already sent.`,
        );
        emit();
        return;
      }

      clearTimeout(entry.timer);
      entries.delete(id);
      emit();
    },
    saveNow(id) {
      const entry = waiting(id);
      if (entry) {
        void save(entry);
      }
    },
    pause(id) {
      const entry = waiting(id);
      if (!entry || entry.action.runningSince === null) {
        return;
      }

      clearTimeout(entry.timer);
      const elapsed = Math.max(0, Date.now() - entry.action.runningSince);
      update(entry, {
        remainingMs: Math.max(0, entry.action.remainingMs - elapsed),
        runningSince: null,
      });
      emit();
    },
    resume(id) {
      const entry = waiting(id);
      if (!entry || entry.action.runningSince !== null) {
        return;
      }

      update(entry, { runningSince: Date.now() });
      startTimer(entry);
      emit();
    },
    flush() {
      return Promise.allSettled(
        [...entries.values()].map((entry) => save(entry)),
      ).then(() => undefined);
    },
    forgetRetries() {
      failed.clear();
      notices = notices.map((notice) => ({ ...notice, canRetry: false }));
      emit();
    },
    setGracePeriodSeconds(seconds) {
      if (
        seconds !== undefined &&
        Number.isInteger(seconds) &&
        seconds >= minimumUndoGracePeriodSeconds &&
        seconds <= maximumUndoGracePeriodSeconds
      ) {
        gracePeriodMs = seconds * 1000;
      }
    },
    connect(nextConnection) {
      connection = nextConnection;
      return () => {
        if (connection === nextConnection) {
          connection = undefined;
        }
      };
    },
    notify(message) {
      addNotice("success", message);
      emit();
    },
    retry(noticeId) {
      const previous = failed.get(noticeId);
      if (!previous) {
        return;
      }

      removeNotice(noticeId);
      nextId += 1;
      const entry: Entry = {
        action: { ...previous.action, id: `undo-${nextId}` },
        commit: previous.commit,
        failure: previous.failure,
      };
      entries.set(entry.action.id, entry);
      void save(entry);
    },
    dismissNotice(id) {
      removeNotice(id);
      emit();
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    getSnapshot() {
      return snapshot;
    },
    reset() {
      entries.forEach((entry) => clearTimeout(entry.timer));
      entries.clear();
      failed.clear();
      notices = [];
      gracePeriodMs = defaultUndoGracePeriodSeconds * 1000;
      connection = undefined;
      emit();
    },
  };
}

export const undoQueue = createUndoQueue();

export interface QueuedActionResult {
  queued: true;
}

/**
 * Queues an API call behind the undo toast and returns the action's result
 * straight away. Route actions validate first and call this instead of
 * awaiting the API, so wrapping an action is one small change.
 */
export function queueUndoableAction<T extends object>(
  result: T,
  action: UndoableAction,
): T & QueuedActionResult {
  undoQueue.enqueue({
    ...action,
    // A lapsed session explains itself better than a route's fallback text.
    failure: (error) =>
      error instanceof AuthApiError ? error.message : action.failure(error),
  });
  return { ...result, queued: true };
}

export function isQueuedActionResult(value: unknown): boolean {
  return (
    typeof value === "object" &&
    value !== null &&
    "queued" in value &&
    value.queued === true
  );
}

/**
 * Nothing changed on the server yet when an action was only queued, so
 * loaders wait for the queue to revalidate after it saves.
 */
export function revalidateUnlessQueued({
  actionResult,
  defaultShouldRevalidate,
}: ShouldRevalidateFunctionArgs): boolean {
  return isQueuedActionResult(actionResult) ? false : defaultShouldRevalidate;
}

export function useUndoQueue(): UndoQueueSnapshot {
  return useSyncExternalStore(undoQueue.subscribe, undoQueue.getSnapshot);
}
