import { useEffect, useRef, useState } from "react";
import type { useFetcher } from "react-router";

import type { TodayJob } from "../../api/today";
import type { TodayActionResult } from "../../app/routes";
import { JobNameFields } from "./JobNameFields";

/**
 * Lets an adult undo an approved job: the job goes back to how it was before it was approved
 * and its points come back off the child's balance. The prompt names the child and points,
 * and starts on the safe choice. It submits through the job card's fetcher, so the card can
 * show a refusal and keep focus once the job's state changes.
 */
export function ReverseApprovalControl({
  job,
  fetcher,
}: {
  job: TodayJob;
  fetcher: ReturnType<typeof useFetcher<TodayActionResult>>;
}) {
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
  const points = `${job.points} ${job.points === 1 ? "point" : "points"}`;
  const keepApproved = () => {
    restoreFocus.current = true;
    setConfirming(false);
  };

  return (
    <div className="reverse-approval">
      {confirming ? (
        <div
          className="inline-confirmation"
          role="group"
          aria-label={`Confirm reversing the approval for ${job.childDisplayName}`}
          onKeyDown={(event) => {
            if (event.key === "Escape" && !isSubmitting) {
              event.preventDefault();
              keepApproved();
            }
          }}
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
              <JobNameFields job={job} />
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
              onClick={keepApproved}
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
          disabled={isSubmitting}
        >
          Reverse approval
        </button>
      )}
    </div>
  );
}
