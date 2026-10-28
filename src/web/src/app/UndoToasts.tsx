import {
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import type { FocusEvent, RefObject } from "react";

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

/** What every toast in the stack shares. */
interface StackControls {
  /** Set when a focused toast goes, so the stack moves focus on after. */
  handOff: RefObject<boolean>;
  holds: Holds;
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
  const handOff = useRef(false);
  const [holds] = useState(createHolds);
  const [showOlder, setShowOlder] = useState(false);
  const controls = { handOff, holds };

  // Newest first, so Tab reaches the latest toast first; the stack is drawn
  // bottom-up, keeping the newest in view and errors at the top.
  const [newest, ...older] = [...waiting].reverse();
  // Fold the next batch again once only one action is left waiting.
  if (showOlder && older.length === 0) {
    setShowOlder(false);
  }

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

  // When a focused toast goes (undone, saved, or folded away) or the folded
  // row does, focus the newest toast left rather than the top of the page.
  // It may have been folded away until now.
  useLayoutEffect(() => {
    if (!handOff.current) {
      return;
    }

    handOff.current = false;
    if (!stack.current?.contains(document.activeElement)) {
      stack.current?.querySelector<HTMLElement>("button")?.focus();
    }
  });

  // When the last toast goes with focus, return focus to where it came from.
  useLayoutEffect(() => {
    const element = stack.current;
    return () => {
      if (element?.contains(document.activeElement)) {
        restoreFocus(returnFocus);
      }
    };
  }, []);

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
      {newest ? (
        <UndoToast key={newest.id} action={newest} controls={controls} />
      ) : null}
      {older.length > 0 ? (
        <FoldedActions
          older={older}
          shown={showOlder}
          onToggle={() => setShowOlder(!showOlder)}
          controls={controls}
        />
      ) : null}
      {showOlder
        ? older.map((action) => (
            <UndoToast key={action.id} action={action} controls={controls} />
          ))
        : null}
      {[...notices].reverse().map((notice) => (
        <NoticeToast key={notice.id} notice={notice} controls={controls} />
      ))}
    </section>
  );
}

/**
 * One compact row in place of every queued action but the newest, so the
 * stack stays short. It holds their countdowns like a toast does, so they
 * can't save (and the row can't vanish) while someone is about to open it.
 */
function FoldedActions({
  older,
  shown,
  onToggle,
  controls,
}: {
  older: QueuedAction[];
  shown: boolean;
  onToggle: () => void;
  controls: StackControls;
}) {
  const row = useRef<HTMLButtonElement>(null);
  const hold = useHold(
    controls.holds,
    older.map((action) => action.id),
  );
  useHandOffWhenGone(row, controls);

  return (
    <button
      ref={row}
      type="button"
      className="toast-stack__more"
      aria-expanded={shown}
      onClick={onToggle}
      {...hold}
    >
      <span>{older.length} more waiting</span> <span aria-hidden="true">·</span>{" "}
      <span className="toast-stack__more-toggle">
        {shown ? "Hide" : "Show"}
      </span>
    </button>
  );
}

/**
 * Asks the stack to move focus on if the element goes while it has focus.
 * A layout cleanup runs before React removes the element, so focus is still
 * there to find.
 */
function useHandOffWhenGone(
  element: RefObject<HTMLElement | null>,
  { handOff }: StackControls,
) {
  useLayoutEffect(() => {
    const node = element.current;
    return () => {
      if (node?.contains(document.activeElement)) {
        handOff.current = true;
      }
    };
  }, [element, handOff]);
}

/** Focus where it was before it entered the stack, else the main content. */
function restoreFocus(returnFocus: RefObject<HTMLElement | null>) {
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

/**
 * Which countdowns are held, and by what: an action's own toast, or the row
 * that folds it away. A countdown resumes only once nothing holds it.
 */
interface Holds {
  set(holder: object, ids: readonly string[]): void;
}

function createHolds(): Holds {
  const held = new Map<object, readonly string[]>();
  return {
    set(holder, ids) {
      const before = held.get(holder) ?? [];
      if (ids.length > 0) {
        held.set(holder, ids);
      } else {
        held.delete(holder);
      }

      ids
        .filter((id) => !before.includes(id))
        .forEach((id) => undoQueue.pause(id));
      before
        .filter(
          (id) =>
            !ids.includes(id) &&
            ![...held.values()].some((others) => others.includes(id)),
        )
        .forEach((id) => undoQueue.resume(id));
    },
  };
}

/**
 * The countdowns wait while someone is reading or about to press a control:
 * while the pointer moves over the element, or it has keyboard focus.
 */
function useHold(holds: Holds, ids: readonly string[]) {
  const [holder] = useState(() => ({}));
  const pointerInside = useRef(false);
  const focusInside = useRef(false);

  // Hold actions that join while the element is held, such as one folded
  // into the row, and let go when the element goes.
  useEffect(() => {
    if (pointerInside.current || focusInside.current) {
      holds.set(holder, ids);
    }
  }, [holds, holder, ids]);
  useEffect(() => () => holds.set(holder, []), [holds, holder]);

  function update() {
    holds.set(holder, pointerInside.current || focusInside.current ? ids : []);
  }

  return {
    // Movement, not entry: an element that appears under a resting cursor
    // shouldn't hold anything.
    onPointerMove() {
      if (!pointerInside.current) {
        pointerInside.current = true;
        update();
      }
    },
    onPointerLeave() {
      pointerInside.current = false;
      update();
    },
    onFocus(event: FocusEvent) {
      // Keyboard focus only: a mouse click, or focus handed on after one,
      // shouldn't hold the countdown.
      if (hasKeyboardFocus(event.target)) {
        focusInside.current = true;
        update();
      }
    },
    onBlur(event: FocusEvent) {
      if (!event.currentTarget.contains(event.relatedTarget)) {
        focusInside.current = false;
        update();
      }
    },
  };
}

function UndoToast({
  action,
  controls,
}: {
  action: QueuedAction;
  controls: StackControls;
}) {
  const descriptionId = useId();
  const toast = useRef<HTMLDivElement>(null);
  const seconds = useRemainingSeconds(action);
  const ids = useMemo(() => [action.id], [action.id]);
  const hold = useHold(controls.holds, ids);
  useHandOffWhenGone(toast, controls);

  return (
    <div ref={toast} className="status-toast undo-toast" {...hold}>
      <p id={descriptionId}>{action.description}</p>
      <span className="undo-toast__countdown" aria-hidden="true">
        {action.runningSince === null ? "Paused" : `${seconds}s`}
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
        aria-describedby={descriptionId}
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
  controls,
}: {
  notice: ToastNotice;
  controls: StackControls;
}) {
  const messageId = useId();
  const toast = useRef<HTMLDivElement>(null);
  useHandOffWhenGone(toast, controls);

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
