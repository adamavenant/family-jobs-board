import { useEffect, useId, useRef } from "react";
import { useFetcher } from "react-router";

import type { TodayJob } from "../../api/today";
import type { TodayActionResult } from "../../app/routes";
import { useUndoQueue } from "../../app/undoQueue";
import { JobNameFields } from "../today/JobNameFields";

/**
 * Approve and reject controls for one pending job, usable away from the daily board. Decisions
 * go through the board's own action, so they share its validation and stale-points guard.
 */
export function ApprovalControls({ job }: { job: TodayJob }) {
  const fetcher = useFetcher<TodayActionResult>();
  const reasonId = useId();
  // A decision waiting behind its undo toast can't be made twice.
  const queued = useUndoQueue().actions.some(
    (action) => action.formData.get("jobId") === job.id,
  );
  const isSubmitting = fetcher.state !== "idle" || queued;
  const submittingIntent = fetcher.formData?.get("intent");
  const error =
    (fetcher.data?.intent === "approve" || fetcher.data?.intent === "reject") &&
    fetcher.data.jobId === job.id
      ? fetcher.data.error
      : undefined;
  const failedIntent = error ? fetcher.data?.intent : undefined;
  const approveRef = useRef<HTMLButtonElement>(null);
  const rejectRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    // The button disables itself while submitting, which drops focus; on failure the job
    // stays, so put focus back on the button that was pressed.
    if (fetcher.state === "idle" && failedIntent && focusIsLost()) {
      (failedIntent === "reject" ? rejectRef : approveRef).current?.focus();
    }
  }, [fetcher.state, failedIntent]);

  return (
    <div className="approval-controls">
      <fetcher.Form method="post" action="/">
        <input type="hidden" name="jobId" value={job.id} />
        <input type="hidden" name="expectedPoints" value={job.points} />
        <JobNameFields job={job} />
        <button
          ref={approveRef}
          type="submit"
          name="intent"
          value="approve"
          disabled={isSubmitting}
        >
          {isSubmitting && submittingIntent === "approve"
            ? "Approving…"
            : `Approve +${job.points}`}
          <JobContext job={job} />
        </button>
      </fetcher.Form>
      <details className="approval-controls__reject">
        <summary>
          Reject…
          <JobContext job={job} />
        </summary>
        <fetcher.Form method="post" action="/">
          <input type="hidden" name="intent" value="reject" />
          <input type="hidden" name="jobId" value={job.id} />
          <JobNameFields job={job} />
          <label htmlFor={reasonId}>
            Rejection reason <span>(optional)</span>
          </label>
          <textarea id={reasonId} name="reason" maxLength={500} rows={2} />
          <button
            ref={rejectRef}
            type="submit"
            className="button--secondary"
            disabled={isSubmitting}
          >
            {isSubmitting && submittingIntent === "reject"
              ? "Rejecting…"
              : "Reject job"}
            <JobContext job={job} />
          </button>
        </fetcher.Form>
      </details>
      {error ? (
        <p role="alert" className="error-message">
          {error}
        </p>
      ) : null}
    </div>
  );
}

/** Names the job for screen readers, since a list repeats the same visible button labels. */
function JobContext({ job }: { job: TodayJob }) {
  return (
    <>
      {" "}
      <span className="visually-hidden">
        {`${job.name} for ${job.childDisplayName}`}
      </span>
    </>
  );
}

/** True when focus has fallen back to the document, typically because its element went away. */
export function focusIsLost(): boolean {
  return (
    document.activeElement === null || document.activeElement === document.body
  );
}
