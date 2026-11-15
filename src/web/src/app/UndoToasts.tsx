import { useEffect, useId, useRef, useState } from "react";
import type { RefObject } from "react";

import { undoQueue, useUndoQueue } from "./undoQueue";
import type { QueuedAction, ToastNotice } from "./undoQueue";

/** The shared stack of undo toasts and messages, shown on every page. */
export function UndoToasts() {
  const { actions, notices } = useUndoQueue();
  const waiting = actions.filter((action) => action.status === "waiting");

  return (
    <>
      <Announcer waiting={waiting} notices={notices} />
      {waiting.length > 0 || notices.length > 0 ? (
        <ToastStack waiting={waiting} notices={notices} />
      ) : null}
    </>
  );
}

interface Announcement {
  order: number;
  text: string;
}

/**
 * One status region that is always on the page, so screen readers reliably
 * announce each new toast (a live region that appears together with its text
 * often goes unannounced). Errors use their own alert instead.
 */
function Announcer({
  waiting,
  notices,
}: {
  waiting: QueuedAction[];
  notices: readonly ToastNotice[];
}) {
  const [announced, setAnnounced] = useState<Announcement | null>(null);
  const newest = [
    ...waiting.map((action) => ({
      order: order(action.id),
      text: `${action.description}. Undo is available for ${Math.round(action.durationMs / 1000)} seconds.`,
    })),
    ...notices
      .filter((notice) => notice.tone === "success")
      .map((notice) => ({ order: order(notice.id), text: notice.message })),
  ].reduce<Announcement | null>(
    (latest, item) => (latest && latest.order > item.order ? latest : item),
    null,
  );
  // Only something newer is announced; closing a toast announces nothing.
  if (newest && (!announced || newest.order > announced.order)) {
    setAnnounced(newest);
  }

  return (
    <p className="visually-hidden" role="status">
      {announced ? <span key={announced.order}>{announced.text}</span> : null}
    </p>
  );
}

function order(id: string): number {
  return Number(id.slice(id.lastIndexOf("-") + 1)) || 0;
}

function ToastStack({
  waiting,
  notices,
}: {
  waiting: QueuedAction[];
  notices: readonly ToastNotice[];
}) {
  const stack = useRef<HTMLElement>(null);
  const returnFocus = useRef<HTMLElement | null>(null);

  // Leave room below the page so content under the stack can scroll into view.
  useEffect(() => {
    const element = stack.current;
    const root = document.documentElement;
    if (!element || typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(() => {
      root.style.setProperty(
        "--toast-stack-height",
        // The stack's height plus its gap above the bottom of the screen.
        `${Math.ceil(element.getBoundingClientRect().height) + 24}px`,
      );
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
      root.style.removeProperty("--toast-stack-height");
    };
  }, []);

  // Newest first, so Tab reaches the latest toast first; the stack is drawn
  // bottom-up, keeping the newest in view and errors at the top.
  return (
    <section
      className="toast-stack"
      aria-label="Notifications"
      ref={stack}
      onFocus={(event) => {
        const from = event.relatedTarget;
        if (
          from instanceof HTMLElement &&
          from !== document.body &&
          !stack.current?.contains(from)
        ) {
          returnFocus.current = from;
        }
      }}
    >
      {[...waiting].reverse().map((action) => (
        <UndoToast
          key={action.id}
          action={action}
          stack={stack}
          returnFocus={returnFocus}
        />
      ))}
      {[...notices].reverse().map((notice) => (
        <NoticeToast
          key={notice.id}
          notice={notice}
          stack={stack}
          returnFocus={returnFocus}
        />
      ))}
    </section>
  );
}

/**
 * Before a toast that holds focus closes, move focus somewhere useful rather
 * than letting it fall to the top of the page: the next toast, else where focus
 * came from, else the page's main content.
 */
function handOffFocus(
  toast: HTMLElement | null,
  stack: RefObject<HTMLElement | null>,
  returnFocus: RefObject<HTMLElement | null>,
) {
  if (!toast || !toast.contains(document.activeElement)) {
    return;
  }

  const next = [
    ...(stack.current?.querySelectorAll<HTMLElement>(".status-toast") ?? []),
  ]
    .find((candidate) => candidate !== toast)
    ?.querySelector<HTMLElement>("button");
  if (next) {
    next.focus();
    return;
  }
  if (returnFocus.current?.isConnected) {
    returnFocus.current.focus();
    return;
  }

  const main = document.querySelector<HTMLElement>("main");
  if (main) {
    main.tabIndex = -1;
    main.focus();
  }
}

function UndoToast({
  action,
  stack,
  returnFocus,
}: {
  action: QueuedAction;
  stack: RefObject<HTMLElement | null>;
  returnFocus: RefObject<HTMLElement | null>;
}) {
  const descriptionId = useId();
  const toast = useRef<HTMLDivElement>(null);
  const pointerInside = useRef(false);
  const focusInside = useRef(false);
  const seconds = useRemainingSeconds(action);

  // The countdown waits while someone is reading or about to press Undo.
  function hold() {
    undoQueue.pause(action.id);
  }

  function release() {
    if (!pointerInside.current && !focusInside.current) {
      undoQueue.resume(action.id);
    }
  }

  return (
    <div
      ref={toast}
      className="status-toast undo-toast"
      // Movement, not entry: a toast that appears under a resting cursor
      // shouldn't pause itself.
      onPointerMove={() => {
        if (!pointerInside.current) {
          pointerInside.current = true;
          hold();
        }
      }}
      onPointerLeave={() => {
        pointerInside.current = false;
        release();
      }}
      onFocus={(event) => {
        // Keyboard focus only: a mouse click, or focus handed on after one,
        // shouldn't hold the countdown.
        if (hasKeyboardFocus(event.target)) {
          focusInside.current = true;
          hold();
        }
      }}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          focusInside.current = false;
          release();
        }
      }}
    >
      <p id={descriptionId}>{action.description}</p>
      <span className="undo-toast__countdown" aria-hidden="true">
        {action.runningSince === null ? "Paused" : `${seconds}s`}
      </span>
      <button
        type="button"
        className="undo-toast__undo"
        aria-describedby={descriptionId}
        onClick={() => {
          handOffFocus(toast.current, stack, returnFocus);
          undoQueue.undo(action.id);
        }}
      >
        Undo
      </button>
      <button
        type="button"
        aria-label="Dismiss and save now"
        aria-describedby={descriptionId}
        title="Dismiss and save now"
        onClick={() => {
          handOffFocus(toast.current, stack, returnFocus);
          undoQueue.saveNow(action.id);
        }}
      >
        ×
      </button>
      <span
        className="undo-toast__timer"
        aria-hidden="true"
        style={{
          transform: `scaleX(${action.durationMs > 0 ? (seconds * 1000) / action.durationMs : 0})`,
        }}
      />
    </div>
  );
}

function hasKeyboardFocus(element: Element): boolean {
  try {
    return element.matches(":focus-visible");
  } catch {
    // Without :focus-visible support, treat all focus as keyboard focus.
    return true;
  }
}

function useRemainingSeconds(action: QueuedAction): number {
  const [now, setNow] = useState(action.runningSince ?? 0);
  const running = action.runningSince !== null;

  useEffect(() => {
    if (!running) {
      return;
    }

    const timer = setInterval(() => setNow(Date.now()), 250);
    return () => clearInterval(timer);
  }, [running]);

  const elapsed =
    action.runningSince === null ? 0 : Math.max(0, now - action.runningSince);
  return Math.max(0, Math.ceil((action.remainingMs - elapsed) / 1000));
}

function NoticeToast({
  notice,
  stack,
  returnFocus,
}: {
  notice: ToastNotice;
  stack: RefObject<HTMLElement | null>;
  returnFocus: RefObject<HTMLElement | null>;
}) {
  const messageId = useId();
  const toast = useRef<HTMLDivElement>(null);
  return (
    <div
      ref={toast}
      className={
        notice.tone === "error"
          ? "status-toast status-toast--error"
          : "status-toast"
      }
    >
      {/* Successes are announced through the shared status region. */}
      <span id={messageId} role={notice.tone === "error" ? "alert" : undefined}>
        {notice.message}
      </span>
      {notice.canRetry ? (
        <button
          type="button"
          className="status-toast__retry"
          aria-describedby={messageId}
          onClick={() => {
            handOffFocus(toast.current, stack, returnFocus);
            undoQueue.retry(notice.id);
          }}
        >
          Try again
        </button>
      ) : null}
      <button
        type="button"
        onClick={() => {
          handOffFocus(toast.current, stack, returnFocus);
          undoQueue.dismissNotice(notice.id);
        }}
        aria-label="Dismiss notification"
      >
        ×
      </button>
    </div>
  );
}
