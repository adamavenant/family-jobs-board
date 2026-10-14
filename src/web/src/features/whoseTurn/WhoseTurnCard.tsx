import { useFetcher } from "react-router";

import type { WhoseTurn } from "../../api/today";
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
          {movesOnWhenTicked && whoseTurn.turnTaken ? (
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
  const submitting = fetcher.state !== "idle";
  const undoable = whoseTurn.undoableTurn;
  const untickHere =
    whoseTurn.turnTaken && undoable?.childId === whoseTurn.childId;
  const showCheckbox =
    (!whoseTurn.turnTaken && whoseTurn.canTakeTurn) || untickHere;
  const pendingIntent = fetcher.formData?.get("intent");
  const checked = submitting ? pendingIntent === "take" : whoseTurn.turnTaken;
  const failure = fetcher.state === "idle" ? fetcher.data?.failure : undefined;

  function submit(intent: "take" | "undo") {
    const form = new FormData();
    form.set("intent", intent);
    form.set("rotationId", whoseTurn.rotationId);
    if (intent === "take") {
      form.set("childId", whoseTurn.childId);
    } else if (undoable) {
      form.set("turnId", undoable.turnId);
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
      {undoable && !untickHere ? (
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
