import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type { HouseholdMember } from "../../api/today";
import { formText } from "../../app/actionDescriptions";
import { createRequestId } from "../../app/requestId";
import { useUndoQueue } from "../../app/undoQueue";
import type { QueuedAction } from "../../app/undoQueue";
import { maximumRewardLength } from "./pointRedemptionsRoute";
import type {
  PointRedemptionActionResult,
  PointRedemptionsLoaderData,
} from "./pointRedemptionsRoute";

function safeRequestId(): string | null {
  try {
    return createRequestId();
  } catch {
    return null;
  }
}

function pointsText(points: number): string {
  return points === 1 ? "1 point" : `${points} points`;
}

function balanceText(name: string, balance: number): string {
  return balance > 0
    ? `${name} has ${pointsText(balance)} to spend.`
    : `${name} doesn't have any points to spend yet.`;
}

/**
 * How much queued redemptions, adjustments, and good-behaviour logs will
 * change a child's points once they are saved.
 */
function queuedPointsChange(
  actions: readonly QueuedAction[],
  childId: string,
): number {
  return actions.reduce((total, { formData: form }) => {
    if (form.get("intent") === "logBehaviour") {
      const points = Number(
        formText(form, "points") || formText(form, "typePoints"),
      );
      return form.getAll("childIds").includes(childId) &&
        Number.isInteger(points)
        ? total + points
        : total;
    }
    if (form.get("childId") !== childId) {
      return total;
    }
    if (form.has("reward")) {
      const points = Number(formText(form, "points"));
      return Number.isInteger(points) ? total - points : total;
    }

    const amount = Number(formText(form, "amount"));
    if (!Number.isInteger(amount)) {
      return total;
    }
    return form.get("direction") === "remove" ? total - amount : total + amount;
  }, 0);
}

export function RedeemRewardPanel({
  children,
}: {
  children: HouseholdMember[];
}) {
  const balances = useFetcher<PointRedemptionsLoaderData>();
  const fetcher = useFetcher<PointRedemptionActionResult>();
  const { actions } = useUndoQueue();
  const formRef = useRef<HTMLFormElement>(null);
  const [requestId, setRequestId] = useState(safeRequestId);
  const [handledResult, setHandledResult] = useState<unknown>(null);
  const [childId, setChildId] = useState("");
  const result = fetcher.data;
  const submitting = fetcher.state !== "idle";
  const child = children.find((candidate) => candidate.id === childId);
  const savedBalance = child ? balances.data?.balances?.[child.id] : undefined;
  const balance =
    child && savedBalance !== undefined
      ? Math.max(0, savedBalance + queuedPointsChange(actions, child.id))
      : undefined;

  // A retry after an error reuses the request ID so the server can record it once;
  // only a queued redemption starts a fresh request.
  if (fetcher.state === "idle" && result?.queued && result !== handledResult) {
    setHandledResult(result);
    setRequestId(safeRequestId());
    setChildId("");
  }

  useEffect(() => {
    if (fetcher.state === "idle" && result?.queued) {
      formRef.current?.reset();
    }
  }, [fetcher.state, result]);

  return (
    <details
      className="grown-up-tools point-redemptions"
      onToggle={(event) => {
        // Reload on every open: points may have changed elsewhere, or the last load failed.
        if (event.currentTarget.open && balances.state === "idle") {
          void balances.load("/point-redemptions");
        }
      }}
    >
      <summary>
        <span className="eyebrow">Rewards</span>
        <span className="grown-up-tools__action">
          Redeem a reward <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <div className="family-members__intro">
          <div>
            <h2>Redeem a reward</h2>
            <p>
              Spend a child’s points on a reward they’ve chosen. A child can
              only spend the points they have. Each redemption is recorded on
              its own and shows in their points history. To fix a mistake, add
              the points back with Adjust points.
            </p>
          </div>
          <fetcher.Form
            method="post"
            action="/point-redemptions"
            className="family-member-form"
            aria-label="Redeem a reward"
            ref={formRef}
          >
            <input type="hidden" name="requestId" value={requestId ?? ""} />
            <input
              type="hidden"
              name={`childName.${childId}`}
              value={child?.displayName ?? ""}
            />
            <div className="form-group">
              <label htmlFor="redemption-child">Child</label>
              <select
                id="redemption-child"
                name="childId"
                required
                value={childId}
                onChange={(event) => setChildId(event.target.value)}
              >
                <option value="">Choose a child</option>
                {children.map((option) => (
                  <option key={option.id} value={option.id}>
                    {option.displayName}
                  </option>
                ))}
              </select>
            </div>
            <div className="form-group">
              <label htmlFor="redemption-points">Points to spend</label>
              <input
                id="redemption-points"
                name="points"
                type="number"
                min="1"
                step="1"
                max={balance !== undefined && balance > 0 ? balance : undefined}
                required
              />
            </div>
            {child ? (
              <p className="redeem-reward__balance" aria-live="polite">
                {balance !== undefined
                  ? balanceText(child.displayName, balance)
                  : balances.data?.error
                    ? "The balance couldn’t be loaded. The points are still checked when you redeem."
                    : `Loading ${child.displayName}’s balance…`}
              </p>
            ) : null}
            <div className="form-group redeem-reward__reward">
              <label htmlFor="redemption-reward">Reward</label>
              <input
                id="redemption-reward"
                name="reward"
                type="text"
                maxLength={maximumRewardLength}
                required
              />
            </div>
            {result?.error ? (
              <p className="error-message" role="alert">
                {result.error}
              </p>
            ) : null}
            <button
              type="submit"
              disabled={submitting || (balance !== undefined && balance < 1)}
            >
              {submitting ? "Saving…" : "Redeem points"}
            </button>
          </fetcher.Form>
        </div>
      </div>
    </details>
  );
}
