import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type { TodayJob } from "../../api/today";
import type { TodayActionResult } from "../../app/routes";

/**
 * Lets an adult undo an approved job: the job goes back to how it was before it was approved
 * and its points come back off the child's balance. The prompt names the child and points,
 * and starts on the safe choice.
 */
export function ReverseApprovalControl({ job }: { job: TodayJob }) {
  const fetcher = useFetcher<TodayActionResult>();
  const [confirming, setConfirming] = useState(false);
  // Close the prompt when revalidation changes what it describes.
  const confirmationKey = `${job.status}:${job.points}`;
  const [confirmationFor, setConfirmationFor] = useState(confirmationKey);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const restoreFocus = useRef(false);
  if (confirmationFor !== confirmationKey) {
    setConfirmationFor(confirmationKey);
    setConfirming(false);
  }

  useEffect(() => {
    if (!confirming && restoreFocus.current) {
      triggerRef.current?.focus();
      restoreFocus.current = false;
    }
  }, [confirming]);
  const isSubmitting = fetcher.state !== "idle";
  const error =
    fetcher.data?.intent === "reverseApproval" && fetcher.data.jobId === job.id
      ? fetcher.data.error
      : undefined;
  const points = `${job.points} ${job.points === 1 ? "point" : "points"}`;

  return (
    <div className="reverse-approval">
      {confirming ? (
        <div
          className="inline-confirmation"
          role="group"
          aria-label={`Confirm reversing the approval for ${job.childDisplayName}`}
        >
          <p>
            Reverse this approval and take {points} back from{" "}
            {job.childDisplayName}?
          </p>
          <div>
            <fetcher.Form method="post" action="/">
              <input type="hidden" name="intent" value="reverseApproval" />
              <input type="hidden" name="jobId" value={job.id} />
              <input type="hidden" name="expectedPoints" value={job.points} />
              <button
                type="submit"
                className="button--danger"
                disabled={isSubmitting}
              >
                {isSubmitting ? "Reversing…" : "Yes, reverse approval"}
              </button>
            </fetcher.Form>
            <button
              type="button"
              className="button--quiet"
              autoFocus
              onClick={() => {
                restoreFocus.current = true;
                setConfirming(false);
              }}
              disabled={isSubmitting}
            >
              Keep approved
            </button>
          </div>
        </div>
      ) : (
        <button
          ref={triggerRef}
          type="button"
          className="reverse-approval__trigger"
          onClick={() => setConfirming(true)}
        >
          Reverse approval
        </button>
      )}
      {error ? (
        <p role="alert" className="error-message">
          {error}
        </p>
      ) : null}
    </div>
  );
}
