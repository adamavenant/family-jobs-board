import { useEffect, useState } from "react";
import { useFetcher } from "react-router";

import type { HouseholdMember } from "../../api/today";
import type { TurnRotationSummary } from "../../api/turnRotation";
import { useSuccessToast } from "../../app/SuccessToast";
import type { WhoseTurnActionResult } from "./whoseTurnRoute";

type WhoseTurnFetcher = ReturnType<typeof useFetcher<WhoseTurnActionResult>>;

export function WhoseTurnPanel({
  children,
  currentDate,
}: {
  children: HouseholdMember[];
  currentDate: string;
}) {
  const fetcher = useFetcher<WhoseTurnActionResult>();
  const { showSuccess } = useSuccessToast();
  const overview = fetcher.data?.overview;
  const hasLoaded = overview !== undefined;
  const message = fetcher.data?.error ? undefined : fetcher.data?.message;

  useEffect(() => {
    if (fetcher.state === "idle" && message) {
      showSuccess(message);
    }
  }, [fetcher.state, message, showSuccess]);

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
              Run as many daily rotations as you like — who is Pink, who sits
              next to Mum — each cycling through its own children. Changes never
              rewrite an answer that already happened.
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
            {overview.rotations.length === 0 ? (
              <p className="inactive-family-members__empty">
                No rotations yet. Add the first one below.
              </p>
            ) : (
              <ul className="whose-turn-rotations" aria-label="Rotations">
                {overview.rotations.map((rotation) => (
                  <RotationItem
                    key={rotation.rotationId}
                    rotation={rotation}
                    fetcher={fetcher}
                    children={children}
                    currentDate={currentDate}
                  />
                ))}
              </ul>
            )}
            <section aria-labelledby="whose-turn-add-heading">
              <h3 id="whose-turn-add-heading">Add a rotation</h3>
              <WhoseTurnForm
                key={overview.rotations.length}
                fetcher={fetcher}
                children={children}
                currentDate={currentDate}
                rotation={null}
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
  fetcher,
  children,
  currentDate,
}: {
  rotation: TurnRotationSummary;
  fetcher: WhoseTurnFetcher;
  children: HouseholdMember[];
  currentDate: string;
}) {
  const [confirmingEnd, setConfirmingEnd] = useState(false);
  const title = rotation.current?.question ?? "Scheduled rotation";
  const submitting = fetcher.state !== "idle";
  const endError =
    fetcher.data?.intent === "end" &&
    fetcher.data.rotationId === rotation.rotationId
      ? fetcher.data.error
      : undefined;

  return (
    <li>
      <div className="family-member-list__identity">
        <strong>{title}</strong>
        {rotation.upcomingTurns[0] ? (
          <span>Today: {rotation.upcomingTurns[0].childDisplayName}</span>
        ) : (
          <span>Not started yet</span>
        )}
      </div>
      <details className="family-member-edit">
        <summary>Edit {title}</summary>
        <WhoseTurnForm
          fetcher={fetcher}
          children={children}
          currentDate={currentDate}
          rotation={rotation}
        />
      </details>
      {confirmingEnd ? (
        <div
          className="deactivate-confirmation"
          role="group"
          aria-label={`End ${title}`}
        >
          <p>
            End “{title}”? It stops appearing on the board from tomorrow; past
            answers stay as they were.
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
        >
          End {title}
        </button>
      )}
    </li>
  );
}

function WhoseTurnForm({
  fetcher,
  children,
  currentDate,
  rotation,
}: {
  fetcher: WhoseTurnFetcher;
  children: HouseholdMember[];
  currentDate: string;
  rotation: TurnRotationSummary | null;
}) {
  const current = rotation?.current ?? null;
  const upcomingTurns = rotation?.upcomingTurns ?? [];
  const idPrefix = `whose-turn-${rotation?.rotationId ?? "new"}`;
  const activeIds = new Set(children.map((child) => child.id));
  const initialOrder = (current?.participantChildIds ?? []).filter((id) =>
    activeIds.has(id),
  );
  const [order, setOrder] = useState<string[]>(initialOrder);
  const [firstChildId, setFirstChildId] = useState(
    current && initialOrder.includes(current.firstChildId)
      ? current.firstChildId
      : (initialOrder[0] ?? ""),
  );
  const [question, setQuestion] = useState(current?.question ?? "");
  const minimumEffectiveFrom = rotation
    ? addDaysIso(currentDate, 1)
    : currentDate;
  const [effectiveFrom, setEffectiveFrom] = useState(minimumEffectiveFrom);
  const submitting = fetcher.state !== "idle";
  const result =
    fetcher.data?.intent === "save" &&
    (fetcher.data.rotationId ?? "") === (rotation?.rotationId ?? "")
      ? fetcher.data
      : undefined;

  function toggleChild(id: string) {
    setOrder((previous) => {
      if (previous.includes(id)) {
        const next = previous.filter((item) => item !== id);
        setFirstChildId((chosen) => (chosen === id ? (next[0] ?? "") : chosen));
        return next;
      }

      return [...previous, id];
    });
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
            onChange={(event) => setFirstChildId(event.target.value)}
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

      {upcomingTurns.length > 0 ? (
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
