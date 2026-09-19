import { useState } from "react";
import { useFetcher } from "react-router";

import type { TodayActionResult } from "../../app/routes";

const requiredConfirmation = "RESET TASKS AND POINTS";

export function AdminDataResetPanel() {
  const fetcher = useFetcher<TodayActionResult>();
  const [confirming, setConfirming] = useState(false);
  const [confirmation, setConfirmation] = useState("");
  const result =
    fetcher.data?.intent === "resetJobsAndPoints" ? fetcher.data : undefined;
  const submitting = fetcher.state !== "idle";
  const confirmed = confirmation === requiredConfirmation;

  return (
    <details className="grown-up-tools admin-data-reset">
      <summary>
        <span className="eyebrow">Danger zone</span>
        <span className="grown-up-tools__action">
          Reset data <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="admin-data-reset__content">
        <div>
          <h2>Reset task and points data</h2>
          <p>
            Clear the board for a fresh start while keeping every family profile
            and PIN.
          </p>
        </div>

        {result?.success ? (
          <p className="success-message" role="status">
            Task and points data was reset.{" "}
            {formatCount(result.deletedJobCount, "job")} and{" "}
            {formatCount(result.deletedPointsEntryCount, "point entry")} were
            removed.
          </p>
        ) : null}

        {!confirming || result?.success ? (
          <button
            type="button"
            className="button--danger-outline"
            onClick={() => {
              fetcher.reset();
              setConfirming(true);
              setConfirmation("");
            }}
          >
            Reset task and points data
          </button>
        ) : (
          <section
            className="admin-data-reset__confirmation"
            aria-labelledby="reset-data-confirmation-heading"
          >
            <h3 id="reset-data-confirmation-heading">This cannot be undone</h3>
            <p>The reset permanently removes:</p>
            <ul>
              <li>One-off jobs and recurring job schedules</li>
              <li>Generated jobs and completion or review history</li>
              <li>Job-earned points entries and child point balances</li>
            </ul>
            <p>Family profiles, PINs, and account access are preserved.</p>

            <fetcher.Form method="post" className="admin-data-reset__form">
              <input type="hidden" name="intent" value="resetJobsAndPoints" />
              <label htmlFor="reset-data-confirmation">
                Type <strong>{requiredConfirmation}</strong> to continue
              </label>
              <input
                id="reset-data-confirmation"
                name="confirmation"
                value={confirmation}
                onChange={(event) => setConfirmation(event.target.value)}
                autoComplete="off"
                spellCheck={false}
              />
              {result?.error ? (
                <p className="error-message" role="alert">
                  {result.error}
                </p>
              ) : null}
              <div className="admin-data-reset__actions">
                <button
                  type="button"
                  className="button--quiet"
                  disabled={submitting}
                  onClick={() => {
                    fetcher.reset();
                    setConfirming(false);
                    setConfirmation("");
                  }}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="button--danger"
                  disabled={!confirmed || submitting}
                >
                  {submitting ? "Resetting…" : "Permanently reset data"}
                </button>
              </div>
            </fetcher.Form>
          </section>
        )}
      </div>
    </details>
  );
}

function formatCount(count: number | undefined, label: string): string {
  const value = count ?? 0;
  return `${value} ${label}${value === 1 ? "" : "s"}`;
}
