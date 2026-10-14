import { useSyncExternalStore } from "react";
import type { ShouldRevalidateFunctionArgs } from "react-router";

// Every in-scope action waits out a household-wide grace period before it is
// sent to the API, so Undo never needs a server endpoint. Route actions queue
// the API call here and return at once; the queue owns the countdown, sends
// the request, and asks the router to revalidate. It is module state rather
// than React context because route actions (outside the React tree) queue
// into it, and queued work must outlive any one page.

export const defaultUndoGracePeriodSeconds = 30;
export const minimumUndoGracePeriodSeconds = 5;
export const maximumUndoGracePeriodSeconds = 120;

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
  saved?: Promise<void>;
}

export interface UndoQueue {
  enqueue(action: UndoableAction): string;
  undo(id: string): void;
  saveNow(id: string): void;
  pause(id: string): void;
  resume(id: string): void;
  /** Sends every queued action now and resolves when they have all settled. */
  flush(): Promise<void>;
  setGracePeriodSeconds(seconds: number | undefined): void;
  connect(revalidate: () => Promise<unknown> | void): () => void;
  notify(message: string): void;
  dismissNotice(id: string): void;
  subscribe(listener: () => void): () => void;
  getSnapshot(): UndoQueueSnapshot;
  /** Drops everything without sending it. For tests. */
  reset(): void;
}

export function createUndoQueue(): UndoQueue {
  const entries = new Map<string, Entry>();
  const listeners = new Set<() => void>();
  let notices: ToastNotice[] = [];
  let snapshot: UndoQueueSnapshot = { actions: [], notices };
  let gracePeriodMs = defaultUndoGracePeriodSeconds * 1000;
  let revalidate: (() => Promise<unknown> | void) | undefined;
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
  }

  function save(entry: Entry): Promise<void> {
    if (entry.saved) {
      return entry.saved;
    }

    clearTimeout(entry.timer);
    update(entry, { status: "saving", runningSince: null });
    emit();
    entry.saved = (async () => {
      try {
        await entry.commit();
      } catch (error) {
        addNotice("error", `Not saved: ${entry.action.description}. ${entry.failure(error)}`);
      }
      try {
        // Show the server's state, whether the action was saved or refused.
        await revalidate?.();
      } finally {
        entries.delete(entry.action.id);
        emit();
      }
    })();
    return entry.saved;
  }

  function addNotice(tone: ToastNotice["tone"], message: string) {
    nextId += 1;
    // One success message at a time, as before; every error stays until dismissed.
    notices = [
      ...notices.filter((notice) => tone === "error" || notice.tone === "error"),
      { id: `notice-${nextId}`, tone, message },
    ];
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
      const entry = waiting(id);
      if (!entry) {
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
    connect(nextRevalidate) {
      revalidate = nextRevalidate;
      return () => {
        if (revalidate === nextRevalidate) {
          revalidate = undefined;
        }
      };
    },
    notify(message) {
      addNotice("success", message);
      emit();
    },
    dismissNotice(id) {
      notices = notices.filter((notice) => notice.id !== id);
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
      notices = [];
      gracePeriodMs = defaultUndoGracePeriodSeconds * 1000;
      revalidate = undefined;
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
  undoQueue.enqueue(action);
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

/**
 * The form of a queued or saving action that matches, like `fetcher.formData`
 * for a submission, so the page can show the action as already done.
 */
export function useQueuedSubmission(
  matches: (form: FormData) => boolean,
): FormData | undefined {
  return useUndoQueue().actions.find((action) => matches(action.formData))
    ?.formData;
}

export function useQueuedSubmissions(): FormData[] {
  return useUndoQueue().actions.map((action) => action.formData);
}
