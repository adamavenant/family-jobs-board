import { useState } from "react";
import { useFetcher } from "react-router";

import type {
  RecurringJobChangeImpact,
  RecurringJobChangeScope,
  TodayJob,
} from "../../api/today";
import type { TodayActionResult } from "../../app/routes";
import { createRequestId } from "../../app/requestId";

const agendaPeriods = [
  ["morning", "Morning"],
  ["arrivingHome", "Arriving home"],
  ["evening", "Evening"],
  ["unscheduled", "No part of day"],
] as const;

const weekdays = [
  ["monday", "Mon"],
  ["tuesday", "Tue"],
  ["wednesday", "Wed"],
  ["thursday", "Thu"],
  ["friday", "Fri"],
  ["saturday", "Sat"],
  ["sunday", "Sun"],
] as const;

const scopes: Array<{
  value: RecurringJobChangeScope;
  label: string;
  description: string;
}> = [
  {
    value: "thisOnly",
    label: "This Only",
    description: "Change only this job. The repeating schedule stays the same.",
  },
  {
    value: "allFuture",
    label: "All Future",
    description: "Include this date and every later job in the schedule.",
  },
  {
    value: "all",
    label: "All",
    description:
      "Include every editable job in the schedule, including past jobs.",
  },
];

export function ScopedRecurringJobEditForm({ job }: { job: TodayJob }) {
  const fetcher = useFetcher<TodayActionResult>();
  const recurrence = job.recurrence;
  const [formRevision, setFormRevision] = useState(0);
  const [frequency, setFrequency] = useState(
    recurrence?.frequency ?? job.recurrenceFrequency ?? "daily",
  );
  if (!recurrence) {
    return <p role="alert">The repeating schedule could not be loaded.</p>;
  }

  return (
    <details>
      <summary>Edit job</summary>
      <fetcher.Form
        method="post"
        className="job-management__form"
        onChange={(event) => {
          const target = event.nativeEvent.target;
          if (
            !(target instanceof Element) ||
            target.getAttribute("name") !== "scope"
          ) {
            setFormRevision((revision) => revision + 1);
          }
        }}
      >
        <RecurringHiddenFields job={job} operation="edit" />
        <label htmlFor={`edit-name-${job.id}`}>Edit job name</label>
        <input
          id={`edit-name-${job.id}`}
          name="name"
          defaultValue={job.name}
          maxLength={160}
          required
        />
        <label htmlFor={`edit-description-${job.id}`}>Edit description</label>
        <textarea
          id={`edit-description-${job.id}`}
          name="description"
          defaultValue={job.description}
          maxLength={1000}
          rows={3}
        />
        <div className="job-management__row">
          <label htmlFor={`edit-points-${job.id}`}>
            Edit points
            <input
              id={`edit-points-${job.id}`}
              name="points"
              type="number"
              min={0}
              step={1}
              defaultValue={job.points}
              required
            />
          </label>
          <label htmlFor={`edit-date-${job.id}`}>
            Edit this job’s date
            <input
              id={`edit-date-${job.id}`}
              name="scheduledDate"
              type="date"
              defaultValue={job.scheduledDate}
              required
            />
          </label>
        </div>
        <p className="form-hint">
          A changed date applies only with This Only. Broader changes use the
          repeating pattern below.
        </p>
        <div className="job-management__row">
          <label htmlFor={`edit-period-${job.id}`}>
            Edit part of day
            <select
              id={`edit-period-${job.id}`}
              name="agendaPeriod"
              defaultValue={job.agendaPeriod}
            >
              {agendaPeriods.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </label>
          <label htmlFor={`edit-time-${job.id}`}>
            Edit time <span>(optional)</span>
            <input
              id={`edit-time-${job.id}`}
              name="scheduledTime"
              type="time"
              defaultValue={job.scheduledTime?.slice(0, 5) ?? ""}
            />
          </label>
        </div>
        <label htmlFor={`edit-frequency-${job.id}`}>Repeats</label>
        <select
          id={`edit-frequency-${job.id}`}
          name="frequency"
          value={frequency}
          onChange={(event) =>
            setFrequency(event.target.value as "daily" | "weekly" | "monthly")
          }
        >
          <option value="daily">Every day</option>
          <option value="weekly">Selected weekdays</option>
          <option value="monthly">Monthly</option>
        </select>
        {frequency === "weekly" ? (
          <fieldset className="weekday-picker">
            <legend>Repeat on</legend>
            {weekdays.map(([value, label]) => (
              <label key={value}>
                <input
                  type="checkbox"
                  name="weekdays"
                  value={value}
                  defaultChecked={recurrence.weekdays.includes(value)}
                />
                {label}
              </label>
            ))}
          </fieldset>
        ) : null}
        {frequency === "monthly" ? (
          <label htmlFor={`edit-month-day-${job.id}`}>
            Day of month
            <input
              id={`edit-month-day-${job.id}`}
              name="dayOfMonth"
              type="number"
              min={1}
              max={31}
              defaultValue={recurrence.dayOfMonth ?? 1}
              required
            />
          </label>
        ) : (
          <input type="hidden" name="dayOfMonth" value="" />
        )}
        <label htmlFor={`edit-end-date-${job.id}`}>
          End date <span>(optional)</span>
          <input
            id={`edit-end-date-${job.id}`}
            name="endDate"
            type="date"
            defaultValue={recurrence.endDate ?? ""}
          />
        </label>
        <ScopeReview
          fetcher={fetcher}
          job={job}
          operation="edit"
          formRevision={formRevision}
        />
      </fetcher.Form>
    </details>
  );
}

export function ScopedRecurringJobCancelForm({ job }: { job: TodayJob }) {
  const fetcher = useFetcher<TodayActionResult>();
  if (!job.recurrence) {
    return <p role="alert">The repeating schedule could not be loaded.</p>;
  }

  return (
    <details>
      <summary>Cancel job</summary>
      <fetcher.Form method="post" className="job-management__form">
        <RecurringHiddenFields job={job} operation="cancel" />
        <p>
          Cancellation is permanent and awards no points. Choose whether to
          cancel this job, this and later jobs, or every editable job in the
          schedule.
        </p>
        <ScopeReview
          fetcher={fetcher}
          job={job}
          operation="cancel"
          formRevision={0}
        />
      </fetcher.Form>
    </details>
  );
}

function RecurringHiddenFields({
  job,
  operation,
}: {
  job: TodayJob;
  operation: "edit" | "cancel";
}) {
  const recurrence = job.recurrence!;
  return (
    <>
      <input type="hidden" name="jobId" value={job.id} />
      <input type="hidden" name="operation" value={operation} />
      <input
        type="hidden"
        name="expectedSeriesVersion"
        value={recurrence.version}
      />
      <input
        type="hidden"
        name="anchorScheduledDate"
        value={job.scheduledDate}
      />
      {operation === "cancel" ? (
        <>
          <input type="hidden" name="name" value={job.name} />
          <input type="hidden" name="description" value={job.description} />
          <input type="hidden" name="points" value={job.points} />
          <input type="hidden" name="scheduledDate" value={job.scheduledDate} />
          <input type="hidden" name="agendaPeriod" value={job.agendaPeriod} />
          <input
            type="hidden"
            name="scheduledTime"
            value={job.scheduledTime?.slice(0, 5) ?? ""}
          />
          <input type="hidden" name="frequency" value={recurrence.frequency} />
          {recurrence.weekdays.map((weekday) => (
            <input
              key={weekday}
              type="hidden"
              name="weekdays"
              value={weekday}
            />
          ))}
          <input
            type="hidden"
            name="dayOfMonth"
            value={recurrence.dayOfMonth ?? ""}
          />
          <input
            type="hidden"
            name="endDate"
            value={recurrence.endDate ?? ""}
          />
        </>
      ) : null}
    </>
  );
}

function ScopeReview({
  fetcher,
  job,
  operation,
  formRevision,
}: {
  fetcher: ReturnType<typeof useFetcher<TodayActionResult>>;
  job: TodayJob;
  operation: "edit" | "cancel";
  formRevision: number;
}) {
  const [previewedRevision, setPreviewedRevision] = useState<number | null>(
    null,
  );
  const [requestId, setRequestId] = useState(safeRequestId);
  const preview =
    fetcher.state === "idle" &&
    previewedRevision === formRevision &&
    fetcher.data?.intent === "previewRecurringJobChange" &&
    fetcher.data.jobId === job.id &&
    fetcher.data.operation === operation
      ? fetcher.data
      : undefined;
  const applied =
    fetcher.data?.intent === "applyRecurringJobChange" &&
    fetcher.data.jobId === job.id &&
    fetcher.data.operation === operation
      ? fetcher.data
      : undefined;
  const busy = fetcher.state !== "idle";

  return (
    <div className="scope-review">
      <input type="hidden" name="requestId" value={requestId} />
      <button
        type="submit"
        name="intent"
        value="previewRecurringJobChange"
        onClick={() => {
          if (applied?.success) {
            setRequestId(safeRequestId());
          }
          setPreviewedRevision(formRevision);
        }}
        disabled={busy}
      >
        {busy && !preview ? "Checking impact…" : "Review changes"}
      </button>
      {preview?.previews ? (
        <fieldset className="scope-picker">
          <legend>Apply to</legend>
          {scopes.map((scope, index) => {
            const impact = preview.previews![scope.value];
            return (
              <label key={scope.value}>
                <input
                  type="radio"
                  name="scope"
                  value={scope.value}
                  defaultChecked={index === 0}
                />
                <span>
                  <strong>{scope.label}</strong>
                  <small>{scope.description}</small>
                  <small>{impactSummary(impact)}</small>
                  {impact.warnings.map((warning) => (
                    <small key={warning} className="scope-picker__warning">
                      {warning}
                    </small>
                  ))}
                </span>
              </label>
            );
          })}
          <p className="form-hint">
            Approved and already-cancelled jobs always remain unchanged. Point
            increases never apply to past jobs.
          </p>
          <button
            type="submit"
            name="intent"
            value="applyRecurringJobChange"
            className={operation === "cancel" ? "button--danger" : undefined}
            disabled={busy || requestId.length === 0}
          >
            {busy
              ? "Applying…"
              : operation === "cancel"
                ? "Confirm cancellation"
                : "Confirm changes"}
          </button>
        </fieldset>
      ) : null}
      {preview?.error || applied?.error ? (
        <p role="alert" className="error-message">
          {preview?.error ?? applied?.error}
        </p>
      ) : null}
      {applied?.success && applied.impact ? (
        <p role="status" className="success-message">
          Saved. {impactSummary(applied.impact)}
        </p>
      ) : null}
    </div>
  );
}

function safeRequestId(): string {
  try {
    return createRequestId();
  } catch {
    return "";
  }
}

function impactSummary(impact: RecurringJobChangeImpact): string {
  const effects = [
    impact.updatedCount > 0 ? `${impact.updatedCount} updated` : null,
    impact.createdCount > 0 ? `${impact.createdCount} created` : null,
    impact.cancelledCount > 0 ? `${impact.cancelledCount} cancelled` : null,
    impact.approvedSkippedCount > 0
      ? `${impact.approvedSkippedCount} approved unchanged`
      : null,
    impact.cancelledSkippedCount > 0
      ? `${impact.cancelledSkippedCount} already cancelled`
      : null,
    impact.retrospectivePointIncreaseSkippedCount > 0
      ? `${impact.retrospectivePointIncreaseSkippedCount} past point value unchanged`
      : null,
  ].filter(Boolean);
  return effects.length > 0 ? effects.join(" · ") : "No stored jobs change.";
}
