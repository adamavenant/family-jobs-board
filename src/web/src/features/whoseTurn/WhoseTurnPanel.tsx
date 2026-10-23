import { useEffect, useState } from "react";
import { useFetcher } from "react-router";

import type { HouseholdMember } from "../../api/today";
import type {
  TurnAdvanceMode,
  TurnRotationSummary,
} from "../../api/turnRotation";
import { formText } from "../../app/actionDescriptions";
import { useUndoQueue } from "../../app/undoQueue";
import type {
  WhoseTurnActionResult,
  WhoseTurnLoaderData,
} from "./whoseTurnRoute";

export function WhoseTurnPanel({
  children,
  currentDate,
}: {
  children: HouseholdMember[];
  currentDate: string;
}) {
  const fetcher = useFetcher<WhoseTurnLoaderData>();
  const { actions } = useUndoQueue();
  const [newFormKey, setNewFormKey] = useState(0);
  const overview = fetcher.data?.overview;
  const hasLoaded = overview !== undefined;
  const rotationActions = actions.filter(
    (action) =>
      action.formData.get("participantChildIds") !== null ||
      action.formData.get("intent") === "end",
  );
  const queuedFor = (rotationId: string) =>
    rotationActions.find(
      (action) => action.formData.get("rotationId") === rotationId,
    )?.formData;
  const rotations = overview?.rotations ?? [];
  const adding = rotationActions.filter(
    (action) =>
      action.formData.get("intent") === "save" &&
      !formText(action.formData, "rotationId"),
  );

  return (
    <details
      className="grown-up-tools whose-turn"
      onToggle={(event) => {
        if (
          event.currentTarget.open &&
          fetcher.state === "idle" &&
          fetcher.data === undefined
        ) {
          void fetcher.load("/whose-turn");
        }
      }}
    >
      <summary>
        <span className="eyebrow">Whose Turn Is It?</span>
        <span className="grown-up-tools__action">
          Manage rotations <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <div className="family-members__intro">
          <div>
            <h2>Whose Turn Is It?</h2>
            <p>
              Run as many rotations as you like — who is Pink, who sits next to
              Mum, who is first in the shower — each cycling through its own
              children, either every day or whenever a turn is ticked off.
              Changes never rewrite an answer that already happened.
            </p>
          </div>
        </div>

        {!hasLoaded && fetcher.data?.error ? (
          <div className="family-members__load-error">
            <p className="error-message" role="alert">
              {fetcher.data.error}
            </p>
            <button
              type="button"
              onClick={() => void fetcher.load("/whose-turn")}
            >
              Try again
            </button>
          </div>
        ) : !hasLoaded ? (
          <p role="status">Loading…</p>
        ) : (
          <>
            {rotations.length === 0 && adding.length === 0 ? (
              <p className="inactive-family-members__empty">
                No rotations yet. Add the first one below.
              </p>
            ) : (
              <ul className="whose-turn-rotations" aria-label="Rotations">
                {rotations.map((rotation) => (
                  <RotationItem
                    key={rotation.rotationId}
                    rotation={rotation}
                    queued={queuedFor(rotation.rotationId) !== undefined}
                    ending={
                      queuedFor(rotation.rotationId)?.get("intent") === "end"
                    }
                    children={children}
                    currentDate={currentDate}
                  />
                ))}
                {adding.map((action) => (
                  <li key={action.id} className="whose-turn--queued">
                    <div className="family-member-list__identity">
                      <strong>
                        {formText(action.formData, "question").trim() ||
                          "Who is Pink today?"}
                      </strong>
                      <span>Being added</span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
            <section aria-labelledby="whose-turn-add-heading">
              <h3 id="whose-turn-add-heading">Add a rotation</h3>
              <WhoseTurnForm
                key={newFormKey}
                children={children}
                currentDate={currentDate}
                rotation={null}
                onQueued={() => setNewFormKey((key) => key + 1)}
              />
            </section>
          </>
        )}
      </div>
    </details>
  );
}

function RotationItem({
  rotation,
  queued,
  ending,
  children,
  currentDate,
}: {
  rotation: TurnRotationSummary;
  queued: boolean;
  ending: boolean;
  children: HouseholdMember[];
  currentDate: string;
}) {
  const fetcher = useFetcher<WhoseTurnActionResult>();
  const [confirmingEnd, setConfirmingEnd] = useState(false);
  const title = rotation.current?.question ?? "Scheduled rotation";
  // A queued change can still be undone; until it is saved the rotation is locked.
  const submitting = fetcher.state !== "idle" || queued;
  // An ended rotation keeps today's answer, so it stays listed until tomorrow; a queued End
  // already shows that.
  const endsTomorrow =
    ending ||
    (rotation.current !== null &&
      !rotation.upcomingTurns.some(
        (turn) => turn.date === addDaysIso(currentDate, 1),
      ));
  const endError =
    fetcher.data?.intent === "end" ? fetcher.data.error : undefined;

  return (
    <li>
      <div className="family-member-list__identity">
        <strong>{title}</strong>
        {queued ? (
          <span>Changes being saved</span>
        ) : rotation.upcomingTurns[0] ? (
          <span>Today: {rotation.upcomingTurns[0].childDisplayName}</span>
        ) : (
          <span>Not started yet</span>
        )}
        {rotation.current?.advanceMode === "whenTicked" ? (
          <span>Moves on when a turn is ticked off</span>
        ) : null}
      </div>
      {endsTomorrow ? (
        <p role="status">Ends tomorrow. Today’s answer stays as it is.</p>
      ) : (
        <>
          <details className="family-member-edit">
            <summary>Edit {title}</summary>
            <WhoseTurnForm
              children={children}
              currentDate={currentDate}
              rotation={rotation}
              locked={queued}
            />
          </details>
          {confirmingEnd ? (
            <div
              className="deactivate-confirmation"
              role="group"
              aria-label={`End ${title}`}
            >
              <p>
                End “{title}”? It stops appearing on the board from tomorrow;
                past answers stay as they were.
              </p>
              {endError ? (
                <p className="error-message" role="alert">
                  {endError}
                </p>
              ) : null}
              <div>
                <fetcher.Form method="post" action="/whose-turn">
                  <input type="hidden" name="intent" value="end" />
                  <input
                    type="hidden"
                    name="rotationId"
                    value={rotation.rotationId}
                  />
                  <input type="hidden" name="rotationTitle" value={title} />
                  <button
                    type="submit"
                    className="button--danger"
                    disabled={submitting}
                  >
                    Yes, end it
                  </button>
                </fetcher.Form>
                <button
                  type="button"
                  className="button--quiet"
                  onClick={() => setConfirmingEnd(false)}
                  disabled={submitting}
                >
                  Keep it
                </button>
              </div>
            </div>
          ) : (
            <button
              type="button"
              className="button--quiet"
              onClick={() => setConfirmingEnd(true)}
              disabled={queued}
            >
              End {title}
            </button>
          )}
        </>
      )}
    </li>
  );
}

function WhoseTurnForm({
  children,
  currentDate,
  rotation,
  locked = false,
  onQueued,
}: {
  children: HouseholdMember[];
  currentDate: string;
  rotation: TurnRotationSummary | null;
  locked?: boolean;
  onQueued?: () => void;
}) {
  const fetcher = useFetcher<WhoseTurnActionResult>();
  const current = rotation?.current ?? null;
  const upcomingTurns = rotation?.upcomingTurns ?? [];
  const upNext = rotation?.upNext ?? [];
  const idPrefix = `whose-turn-${rotation?.rotationId ?? "new"}`;
  const activeIds = new Set(children.map((child) => child.id));
  const initialOrder = (current?.participantChildIds ?? []).filter((id) =>
    activeIds.has(id),
  );
  const [order, setOrder] = useState<string[]>(initialOrder);
  // Only a first turn the adult picks is kept in state. Otherwise an existing rotation's
  // default is worked out from the latest overview on every render: the form stays mounted
  // while its details are closed, and a tick on the board can change who is up next.
  const [chosenFirstChildId, setChosenFirstChildId] = useState<string | null>(
    null,
  );
  // A rotation that moves on when ticked carries on from whoever is next, not from the
  // child it originally started with.
  const projectedFirstChildId =
    current?.advanceMode === "whenTicked"
      ? upNext.find((child) => order.includes(child.childId))?.childId
      : current?.firstChildId;
  const firstChildId =
    [
      chosenFirstChildId,
      ...(rotation ? [projectedFirstChildId, order[0]] : []),
    ].find((id) => id && order.includes(id)) ?? "";
  const [question, setQuestion] = useState(current?.question ?? "");
  const [advanceMode, setAdvanceMode] = useState<TurnAdvanceMode>(
    current?.advanceMode === "whenTicked" ? "whenTicked" : "daily",
  );
  const minimumEffectiveFrom = rotation
    ? addDaysIso(currentDate, 1)
    : currentDate;
  const [effectiveFrom, setEffectiveFrom] = useState(minimumEffectiveFrom);
  const submitting = fetcher.state !== "idle" || locked;
  const result = fetcher.data?.intent === "save" ? fetcher.data : undefined;

  // A new rotation's form starts again once its rotation is queued.
  useEffect(() => {
    if (fetcher.state === "idle" && result?.queued) {
      onQueued?.();
    }
  }, [fetcher.state, onQueued, result]);

  function toggleChild(id: string) {
    if (!order.includes(id)) {
      setOrder([...order, id]);
      return;
    }

    const next = order.filter((item) => item !== id);
    setOrder(next);
    if (chosenFirstChildId === id) {
      // An existing rotation falls back to its projected default; a new one to the next child.
      setChosenFirstChildId(rotation ? null : (next[0] ?? null));
    }
  }

  function move(id: string, direction: -1 | 1) {
    setOrder((previous) => {
      const index = previous.indexOf(id);
      const target = index + direction;
      if (index < 0 || target < 0 || target >= previous.length) {
        return previous;
      }

      const next = [...previous];
      [next[index], next[target]] = [next[target]!, next[index]!];
      return next;
    });
  }

  return (
    <>
      <fetcher.Form
        method="post"
        action="/whose-turn"
        className="family-member-form"
        aria-label={
          rotation
            ? `Configure ${current?.question ?? "rotation"}`
            : "Add a rotation"
        }
      >
        <input type="hidden" name="intent" value="save" />
        <input
          type="hidden"
          name="rotationId"
          value={rotation?.rotationId ?? ""}
        />
        <input
          type="hidden"
          name="rotationTitle"
          value={current?.question ?? ""}
        />
        <fieldset className="assignee-picker">
          <legend>Participants</legend>
          <div className="assignee-picker__options">
            {children.map((child) => (
              <label key={child.id} className="assignee-option">
                <input
                  type="checkbox"
                  checked={order.includes(child.id)}
                  onChange={() => toggleChild(child.id)}
                />
                <span>{child.displayName}</span>
              </label>
            ))}
          </div>
        </fieldset>

        {order.length > 0 ? (
          <fieldset>
            <legend>Order</legend>
            <ol className="whose-turn-order">
              {order.map((id, index) => {
                const child = children.find((item) => item.id === id);
                const displayName = child?.displayName ?? "Unknown";
                return (
                  <li key={id}>
                    <span>{displayName}</span>
                    <button
                      type="button"
                      className="button--quiet"
                      onClick={() => move(id, -1)}
                      disabled={index === 0}
                      aria-label={`Move ${displayName} earlier in the order`}
                    >
                      ↑
                    </button>
                    <button
                      type="button"
                      className="button--quiet"
                      onClick={() => move(id, 1)}
                      disabled={index === order.length - 1}
                      aria-label={`Move ${displayName} later in the order`}
                    >
                      ↓
                    </button>
                    <input
                      type="hidden"
                      name="participantChildIds"
                      value={id}
                    />
                  </li>
                );
              })}
            </ol>
          </fieldset>
        ) : null}

        <div className="form-group">
          <label htmlFor={`${idPrefix}-first`}>First turn</label>
          <select
            id={`${idPrefix}-first`}
            name="firstChildId"
            value={firstChildId}
            onChange={(event) => setChosenFirstChildId(event.target.value)}
            required
            disabled={order.length === 0}
          >
            <option value="" disabled>
              Choose who starts
            </option>
            {order.map((id) => {
              const child = children.find((item) => item.id === id);
              return (
                <option key={id} value={id}>
                  {child?.displayName ?? id}
                </option>
              );
            })}
          </select>
        </div>

        <fieldset className="assignee-picker">
          <legend>Moves on</legend>
          <div className="assignee-picker__options">
            <label className="assignee-option">
              <input
                type="radio"
                name="advanceMode"
                value="daily"
                checked={advanceMode === "daily"}
                onChange={() => setAdvanceMode("daily")}
              />
              <span>Every day</span>
            </label>
            <label className="assignee-option">
              <input
                type="radio"
                name="advanceMode"
                value="whenTicked"
                checked={advanceMode === "whenTicked"}
                onChange={() => setAdvanceMode("whenTicked")}
              />
              <span>When a turn is ticked off</span>
            </label>
          </div>
          <small>
            {advanceMode === "whenTicked"
              ? "For turns that don’t happen every day. A grown-up or the child whose turn it is ticks it off on the board. With two children the turn passes straight to the other child; with three or more, the next child is up from the following day."
              : "Every calendar day moves on to the next child."}
          </small>
        </fieldset>

        <div className="form-group">
          <label htmlFor={`${idPrefix}-effective`}>Effective from</label>
          <input
            id={`${idPrefix}-effective`}
            name="effectiveFrom"
            type="date"
            min={minimumEffectiveFrom}
            value={effectiveFrom}
            onChange={(event) => setEffectiveFrom(event.target.value)}
            required
          />
          <small>
            {rotation
              ? "A change starts tomorrow at the earliest, so today's answer stays put."
              : "A new rotation can start today."}
          </small>
        </div>

        <div className="form-group">
          <label htmlFor={`${idPrefix}-question`}>Question (optional)</label>
          <input
            id={`${idPrefix}-question`}
            name="question"
            maxLength={200}
            value={question}
            onChange={(event) => setQuestion(event.target.value)}
            placeholder="Who is Pink today?"
          />
        </div>

        {result?.error ? (
          <p className="error-message" role="alert">
            {result.error}
          </p>
        ) : null}
        <button
          type="submit"
          disabled={submitting || order.length === 0 || !firstChildId}
        >
          {rotation ? "Save changes" : "Add rotation"}
        </button>
      </fetcher.Form>

      {upNext.length > 0 ? (
        <section aria-labelledby={`${idPrefix}-up-next-heading`}>
          <h3 id={`${idPrefix}-up-next-heading`}>Up next</h3>
          <ol className="whose-turn-preview">
            {upNext.map((child) => (
              <li key={child.childId}>{child.childDisplayName}</li>
            ))}
          </ol>
        </section>
      ) : upcomingTurns.length > 0 ? (
        <section aria-labelledby={`${idPrefix}-preview-heading`}>
          <h3 id={`${idPrefix}-preview-heading`}>Upcoming turns</h3>
          <ul className="whose-turn-preview">
            {upcomingTurns.map((turn) => (
              <li key={turn.date}>
                <span>{formatShortDate(turn.date)}</span>
                <span>{turn.childDisplayName}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </>
  );
}

function addDaysIso(date: string, days: number): string {
  const [year, month, day] = date.split("-").map(Number);
  const utc = new Date(Date.UTC(year!, (month ?? 1) - 1, day ?? 1));
  utc.setUTCDate(utc.getUTCDate() + days);
  return utc.toISOString().slice(0, 10);
}

function formatShortDate(date: string): string {
  return new Intl.DateTimeFormat("en", {
    weekday: "short",
    day: "numeric",
    month: "short",
  }).format(new Date(`${date}T12:00:00`));
}
