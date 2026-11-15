import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type { HouseholdMember } from "../../api/today";
import { createRequestId } from "../../app/requestId";
import type { PointAdjustmentActionResult } from "./pointAdjustmentsRoute";

function safeRequestId(): string | null {
  try {
    return createRequestId();
  } catch {
    return null;
  }
}

export function PointAdjustmentsPanel({
  children,
}: {
  children: HouseholdMember[];
}) {
  const fetcher = useFetcher<PointAdjustmentActionResult>();
  const formRef = useRef<HTMLFormElement>(null);
  const [requestId, setRequestId] = useState(safeRequestId);
  const [handledResult, setHandledResult] = useState<unknown>(null);
  const result = fetcher.data;
  const submitting = fetcher.state !== "idle";

  // A retry after an error reuses the request ID so the server can record it once;
  // only a queued adjustment starts a fresh request.
  if (fetcher.state === "idle" && result?.queued && result !== handledResult) {
    setHandledResult(result);
    setRequestId(safeRequestId());
  }

  useEffect(() => {
    if (fetcher.state === "idle" && result?.queued) {
      formRef.current?.reset();
    }
  }, [fetcher.state, result]);

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
              Add or remove points with a reason. Points never go below zero.
              Each change is recorded on its own and never edited. To fix a
              mistake, record a new adjustment that undoes it.
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
            {children.map((child) => (
              <input
                key={child.id}
                type="hidden"
                name={`childName.${child.id}`}
                value={child.displayName}
              />
            ))}
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
      </div>
    </details>
  );
}
