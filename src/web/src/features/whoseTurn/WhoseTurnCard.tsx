import { useFetcher } from "react-router";

import type { WhoseTurn } from "../../api/today";
import { useUndoQueue } from "../../app/undoQueue";
import type { WhoseTurnTickResult } from "./whoseTurnRoute";

export function WhoseTurnCard({
  whoseTurn,
  date,
  isToday,
}: {
  whoseTurn: WhoseTurn;
  date: string;
  isToday: boolean;
}) {
  const movesOnWhenTicked = whoseTurn.advanceMode === "whenTicked";
  const queuedTick = useQueuedTick(whoseTurn.rotationId);
  const turnTaken = queuedTick ? queuedTick === "take" : whoseTurn.turnTaken;

  return (
    <div className="whose-turn-card">
      <div className="whose-turn-card__answer" role="status">
        <span className="whose-turn-card__question">
          {phraseForDate(whoseTurn.question, date, isToday)}
        </span>
        <span className="whose-turn-card__child">
          <span className="whose-turn-card__badge" aria-hidden="true">
            {whoseTurn.childDisplayName.charAt(0).toUpperCase()}
          </span>
          {whoseTurn.childDisplayName}
          {movesOnWhenTicked && turnTaken ? (
            <span className="whose-turn-card__taken">
              <span aria-hidden="true">✓</span> had their turn
            </span>
          ) : null}
        </span>
      </div>
      {movesOnWhenTicked && isToday ? <TurnTick whoseTurn={whoseTurn} /> : null}
    </div>
  );
}

/**
 * Ticks today's turn off, or unticks the latest tick. With one or three or more children a
 * ticked turn stays on the card until tomorrow, so the same checkbox unticks it; with two
 * children the card moves straight on, so the previous tick is offered as an undo.
 */
function TurnTick({ whoseTurn }: { whoseTurn: WhoseTurn }) {
  const fetcher = useFetcher<WhoseTurnTickResult>();
  // A tick waits behind the undo toast, so show it as done and lock the card
  // until it has been saved or undone.
  const queuedTick = useQueuedTick(whoseTurn.rotationId);
  const submitting = fetcher.state !== "idle" || queuedTick !== undefined;
  const undoable = whoseTurn.undoableTurn;
  const untickHere =
    whoseTurn.turnTaken && undoable?.childId === whoseTurn.childId;
  const showCheckbox =
    (!whoseTurn.turnTaken && whoseTurn.canTakeTurn) || untickHere;
  const pendingIntent = queuedTick ?? fetcher.formData?.get("intent");
  const checked = submitting ? pendingIntent === "take" : whoseTurn.turnTaken;
  const failure = fetcher.state === "idle" ? fetcher.data?.failure : undefined;

  function submit(intent: "take" | "undo") {
    const form = new FormData();
    form.set("intent", intent);
    form.set("rotationId", whoseTurn.rotationId);
    // Names the undo toast uses to describe the tick.
    form.set("question", whoseTurn.question);
    if (intent === "take") {
      form.set("revisionId", whoseTurn.revisionId);
      form.set("turnNumber", String(whoseTurn.turnNumber));
      form.set("childId", whoseTurn.childId);
      form.set("childName", whoseTurn.childDisplayName);
    } else if (undoable) {
      form.set("turnId", undoable.turnId);
      form.set("childName", undoable.childDisplayName);
    }
    void fetcher.submit(form, { method: "post", action: "/whose-turn/turns" });
  }

  if (!showCheckbox && !undoable && !failure) {
    return null;
  }

  return (
    <div className="whose-turn-card__turn">
      {showCheckbox ? (
        <label className="whose-turn-card__tick">
          <input
            type="checkbox"
            checked={checked}
            disabled={submitting}
            onChange={(event) =>
              submit(event.currentTarget.checked ? "take" : "undo")
            }
          />
          <span>{whoseTurn.childDisplayName} has had their turn</span>
        </label>
      ) : null}
      {undoable && !untickHere && queuedTick !== "undo" ? (
        <p className="whose-turn-card__undo">
          <span>{undoable.childDisplayName} just had a turn.</span>
          <button
            type="button"
            className="button--quiet"
            disabled={submitting}
            onClick={() => submit("undo")}
          >
            Undo {undoable.childDisplayName}’s turn
          </button>
        </p>
      ) : null}
      {failure ? (
        <p className="error-message" role="alert">
          {failure}
        </p>
      ) : null}
    </div>
  );
}

/** The tick or untick waiting behind an undo toast for this rotation, if any. */
function useQueuedTick(rotationId: string): "take" | "undo" | undefined {
  const intent = useUndoQueue()
    .actions.map((action) => action.formData)
    .find(
      (form) =>
        form.get("rotationId") === rotationId &&
        (form.get("intent") === "take" || form.get("intent") === "undo"),
    )
    ?.get("intent");
  return intent === "take" || intent === "undo" ? intent : undefined;
}

function phraseForDate(
  question: string,
  date: string,
  isToday: boolean,
): string {
  const trimmed = question.trim();
  if (isToday) {
    return trimmed;
  }

  const weekday = new Intl.DateTimeFormat("en", { weekday: "long" }).format(
    new Date(`${date}T12:00:00`),
  );
  if (/today\?$/i.test(trimmed)) {
    return trimmed.replace(/today\?$/i, `on ${weekday}?`);
  }
  if (/today$/i.test(trimmed)) {
    return trimmed.replace(/today$/i, `on ${weekday}`);
  }

  return `${trimmed} (${weekday})`;
}
