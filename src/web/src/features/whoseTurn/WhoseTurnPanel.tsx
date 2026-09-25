import { useEffect, useState } from "react";
import { useFetcher } from "react-router";

import type { HouseholdMember } from "../../api/today";
import type {
  TurnRotationConfiguration,
  TurnRotationTurn,
} from "../../api/turnRotation";
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

  useEffect(() => {
    if (fetcher.state === "idle" && fetcher.data?.saved) {
      showSuccess("Whose Turn Is It? rotation saved.");
    }
  }, [fetcher.state, fetcher.data, showSuccess]);

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
          {overview?.current ? "Manage the rotation" : "Set it up"}{" "}
          <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <div className="family-members__intro">
          <div>
            <h2>Whose Turn Is It?</h2>
            <p>
              Choose which children take turns and the order they cycle through.
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
          <WhoseTurnForm
            key={overview.current?.id ?? "unset"}
            fetcher={fetcher}
            children={children}
            currentDate={currentDate}
            current={overview.current}
            upcomingTurns={overview.upcomingTurns}
          />
        )}
      </div>
    </details>
  );
}

function WhoseTurnForm({
  fetcher,
  children,
  currentDate,
  current,
  upcomingTurns,
}: {
  fetcher: WhoseTurnFetcher;
  children: HouseholdMember[];
  currentDate: string;
  current: TurnRotationConfiguration | null;
  upcomingTurns: TurnRotationTurn[];
}) {
  const [order, setOrder] = useState<string[]>(
    current?.participantChildIds ?? [],
  );
  const [firstChildId, setFirstChildId] = useState(current?.firstChildId ?? "");
  const [question, setQuestion] = useState(current?.question ?? "");
  const minimumEffectiveFrom = current
    ? addDaysIso(currentDate, 1)
    : currentDate;
  const [effectiveFrom, setEffectiveFrom] = useState(minimumEffectiveFrom);
  const submitting = fetcher.state !== "idle";

  function toggleChild(id: string) {
    setOrder((previous) => {
      if (previous.includes(id)) {
        const next = previous.filter((item) => item !== id);
        setFirstChildId((current) =>
          current === id ? (next[0] ?? "") : current,
        );
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
        aria-label="Configure whose turn is it"
      >
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
          <label htmlFor="whose-turn-first">First turn</label>
          <select
            id="whose-turn-first"
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
          <label htmlFor="whose-turn-effective">Effective from</label>
          <input
            id="whose-turn-effective"
            name="effectiveFrom"
            type="date"
            min={minimumEffectiveFrom}
            value={effectiveFrom}
            onChange={(event) => setEffectiveFrom(event.target.value)}
            required
          />
          <small>
            {current
              ? "A change starts tomorrow at the earliest, so today's answer stays put."
              : "The first setup can start today."}
          </small>
        </div>

        <div className="form-group">
          <label htmlFor="whose-turn-question">Question (optional)</label>
          <input
            id="whose-turn-question"
            name="question"
            maxLength={200}
            value={question}
            onChange={(event) => setQuestion(event.target.value)}
            placeholder="Who is Pink today?"
          />
        </div>

        {fetcher.data?.error ? (
          <p className="error-message" role="alert">
            {fetcher.data.error}
          </p>
        ) : null}
        <button
          type="submit"
          disabled={submitting || order.length === 0 || !firstChildId}
        >
          {current ? "Save changes" : "Set up rotation"}
        </button>
      </fetcher.Form>

      {upcomingTurns.length > 0 ? (
        <section aria-labelledby="whose-turn-preview-heading">
          <h3 id="whose-turn-preview-heading">Upcoming turns</h3>
          <ul className="whose-turn-preview">
            {upcomingTurns.map((turn) => {
              const child = children.find((item) => item.id === turn.childId);
              return (
                <li key={turn.date}>
                  <span>{formatShortDate(turn.date)}</span>
                  <span>{child?.displayName ?? "Unknown"}</span>
                </li>
              );
            })}
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
