import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type { HouseholdMember } from "../../api/today";
import { createRequestId } from "../../app/requestId";
import { useSuccessToast } from "../../app/SuccessToast";
import type { PointAdjustmentActionResult } from "./pointAdjustmentsRoute";

function safeRequestId(): string | null {
  try {
    return createRequestId();
  } catch {
    return null;
  }
}

function recordedMessage(
  recorded: PointAdjustmentActionResult["recorded"],
  children: HouseholdMember[],
): string | undefined {
  if (!recorded) {
    return undefined;
  }

  const name =
    children.find((child) => child.id === recorded.childId)?.displayName ??
    "the child";
  const points = Math.abs(recorded.amount);
  const unit = points === 1 ? "point" : "points";
  const change =
    recorded.amount < 0
      ? `Removed ${points} ${unit} from ${name}.`
      : `Added ${points} ${unit} for ${name}.`;
  return `${change} Balance is now ${recorded.pointsBalance}.`;
}

export function PointAdjustmentsPanel({
  children,
}: {
  children: HouseholdMember[];
}) {
  const fetcher = useFetcher<PointAdjustmentActionResult>();
  const { showSuccess } = useSuccessToast();
  const formRef = useRef<HTMLFormElement>(null);
  const [requestId, setRequestId] = useState(safeRequestId);
  const [handledResult, setHandledResult] = useState<unknown>(null);
  const [dismissedResult, setDismissedResult] = useState<unknown>(null);
  const result = fetcher.data;
  const submitting = fetcher.state !== "idle";
  const message = recordedMessage(result?.recorded, children);
  const warning =
    result?.needsConfirmation && result !== dismissedResult
      ? result.needsConfirmation
      : undefined;

  // A retry after an error reuses the request ID so the server can record it once;
  // only a recorded adjustment starts a fresh request.
  if (
    fetcher.state === "idle" &&
    result?.recorded &&
    result !== handledResult
  ) {
    setHandledResult(result);
    setRequestId(safeRequestId());
  }

  useEffect(() => {
    if (fetcher.state === "idle" && message) {
      formRef.current?.reset();
      showSuccess(message);
    }
  }, [fetcher.state, message, showSuccess]);

  return (
    <details className="grown-up-tools point-adjustments">
      <summary>
        <span className="eyebrow">Points</span>
        <span className="grown-up-tools__action">
          Adjust points <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <div className="family-members__intro">
          <div>
            <h2>Adjust points</h2>
            <p>
              Add or remove points with a reason. Each change is recorded on its
              own and never edited. To fix a mistake, record a new adjustment
              that undoes it.
            </p>
          </div>
          <fetcher.Form
            method="post"
            action="/point-adjustments"
            className="family-member-form"
            aria-label="Adjust points"
            ref={formRef}
          >
            <input type="hidden" name="requestId" value={requestId ?? ""} />
            <div className="form-group">
              <label htmlFor="adjustment-child">Child</label>
              <select id="adjustment-child" name="childId" required>
                {children.map((child) => (
                  <option key={child.id} value={child.id}>
                    {child.displayName}
                  </option>
                ))}
              </select>
            </div>
            <fieldset className="adjustment-direction">
              <legend>Change</legend>
              <label>
                <input
                  type="radio"
                  name="direction"
                  value="add"
                  defaultChecked
                />
                <span>Add points</span>
              </label>
              <label>
                <input type="radio" name="direction" value="remove" />
                <span>Remove points</span>
              </label>
            </fieldset>
            <div className="form-group">
              <label htmlFor="adjustment-amount">Number of points</label>
              <input
                id="adjustment-amount"
                name="amount"
                type="number"
                min="1"
                step="1"
                required
              />
            </div>
            <div className="form-group adjustment-reason">
              <label htmlFor="adjustment-reason">Reason</label>
              <textarea
                id="adjustment-reason"
                name="reason"
                rows={2}
                maxLength={500}
                required
              />
            </div>
            {result?.error ? (
              <p className="error-message" role="alert">
                {result.error}
              </p>
            ) : null}
            <button type="submit" disabled={submitting}>
              {submitting ? "Saving…" : "Record adjustment"}
            </button>
          </fetcher.Form>
        </div>

        {warning ? (
          <div
            className="deactivate-confirmation adjustment-warning"
            role="alert"
            aria-label="Negative balance warning"
          >
            <h3>This will make the balance negative</h3>
            <p>{warning.message}</p>
            <div>
              <fetcher.Form method="post" action="/point-adjustments">
                <input
                  type="hidden"
                  name="requestId"
                  value={warning.submitted.requestId}
                />
                <input
                  type="hidden"
                  name="childId"
                  value={warning.submitted.childId}
                />
                <input
                  type="hidden"
                  name="direction"
                  value={warning.submitted.amount < 0 ? "remove" : "add"}
                />
                <input
                  type="hidden"
                  name="amount"
                  value={Math.abs(warning.submitted.amount)}
                />
                <input
                  type="hidden"
                  name="reason"
                  value={warning.submitted.reason}
                />
                <button
                  type="submit"
                  name="confirm"
                  value="true"
                  className="button--danger"
                  disabled={submitting}
                >
                  {submitting ? "Saving…" : "Yes, adjust anyway"}
                </button>
              </fetcher.Form>
              <button
                type="button"
                className="button--quiet"
                onClick={() => setDismissedResult(result)}
                disabled={submitting}
              >
                Cancel
              </button>
            </div>
          </div>
        ) : null}
      </div>
    </details>
  );
}
