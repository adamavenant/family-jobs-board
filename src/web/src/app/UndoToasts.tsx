import { useEffect, useId, useRef, useState } from "react";

import { undoQueue, useUndoQueue } from "./undoQueue";
import type { QueuedAction, ToastNotice } from "./undoQueue";

/** The shared stack of undo toasts and messages, shown on every page. */
export function UndoToasts() {
  const { actions, notices } = useUndoQueue();
  const waiting = actions.filter((action) => action.status === "waiting");
  if (waiting.length === 0 && notices.length === 0) {
    return null;
  }

  return (
    <section className="toast-stack" aria-label="Notifications">
      {notices.map((notice) => (
        <NoticeToast key={notice.id} notice={notice} />
      ))}
      {waiting.map((action) => (
        <UndoToast key={action.id} action={action} />
      ))}
    </section>
  );
}

function UndoToast({ action }: { action: QueuedAction }) {
  const descriptionId = useId();
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
      className="status-toast undo-toast"
      onPointerEnter={() => {
        pointerInside.current = true;
        hold();
      }}
      onPointerLeave={() => {
        pointerInside.current = false;
        release();
      }}
      onFocus={() => {
        focusInside.current = true;
        hold();
      }}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          focusInside.current = false;
          release();
        }
      }}
    >
      <p role="status" id={descriptionId}>
        {action.description}
      </p>
      <span
        className="undo-toast__countdown"
        aria-hidden="true"
        data-paused={action.runningSince === null ? "" : undefined}
      >
        {seconds}s
      </span>
      <button
        type="button"
        className="undo-toast__undo"
        aria-describedby={descriptionId}
        onClick={() => undoQueue.undo(action.id)}
      >
        Undo
      </button>
      <button
        type="button"
        aria-label="Dismiss and save now"
        title="Dismiss and save now"
        onClick={() => undoQueue.saveNow(action.id)}
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

function NoticeToast({ notice }: { notice: ToastNotice }) {
  return (
    <div
      className={
        notice.tone === "error"
          ? "status-toast status-toast--error"
          : "status-toast"
      }
    >
      <span role={notice.tone === "error" ? "alert" : "status"}>
        {notice.message}
      </span>
      {notice.canRetry ? (
        <button
          type="button"
          className="status-toast__retry"
          onClick={() => undoQueue.retry(notice.id)}
        >
          Try again
        </button>
      ) : null}
      <button
        type="button"
        onClick={() => undoQueue.dismissNotice(notice.id)}
        aria-label="Dismiss notification"
      >
        ×
      </button>
    </div>
  );
}
