import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type { TodayActionResult } from "../../app/routes";
import type { HouseholdMember } from "../../api/today";

const weekdays = [
  ["monday", "Monday"],
  ["tuesday", "Tuesday"],
  ["wednesday", "Wednesday"],
  ["thursday", "Thursday"],
  ["friday", "Friday"],
  ["saturday", "Saturday"],
  ["sunday", "Sunday"],
] as const;

export function RecurringJobForm({
  children,
  viewerId,
  today,
}: {
  children: HouseholdMember[];
  viewerId: string;
  today: string;
}) {
  const fetcher = useFetcher<TodayActionResult>();
  const formRef = useRef<HTMLFormElement>(null);
  const [frequency, setFrequency] = useState<"daily" | "weekly">("daily");
  const result =
    fetcher.data?.intent === "addRecurring" ? fetcher.data : undefined;
  const isSubmitting = fetcher.state !== "idle";
  const frequencyLabel = frequency === "weekly" ? "Weekly" : "Daily";

  useEffect(() => {
    if (fetcher.state === "idle" && result?.success) {
      formRef.current?.reset();
    }
  }, [fetcher.state, result?.success]);

  return (
    <details className="grown-up-tools grown-up-tools--recurring">
      <summary>
        <span className="eyebrow">Routines</span>
        <span className="grown-up-tools__action">
          Add a recurring job <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="add-job" aria-labelledby="add-recurring-job-heading">
        <div className="add-job__heading">
          <h2 id="add-recurring-job-heading">Add a recurring job</h2>
          <p>
            Create jobs on a daily or weekly schedule for the next eight weeks.
          </p>
        </div>
        <fetcher.Form
          method="post"
          className="add-job__form"
          ref={formRef}
          onReset={() => setFrequency("daily")}
        >
          <input type="hidden" name="intent" value="addRecurring" />
          <input type="hidden" name="viewerId" value={viewerId} />
          <div className="form-group">
            <label htmlFor="recurrenceFrequency">Repeats</label>
            <select
              id="recurrenceFrequency"
              name="recurrenceFrequency"
              value={frequency}
              onChange={(event) =>
                setFrequency(
                  event.target.value === "weekly" ? "weekly" : "daily",
                )
              }
            >
              <option value="daily">Every day</option>
              <option value="weekly">Selected weekdays</option>
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="recurringChildId">
              Assign {frequencyLabel.toLowerCase()} job to
            </label>
            <select
              id="recurringChildId"
              name="childId"
              required
              defaultValue={children[0]?.id}
            >
              {children.map((child) => (
                <option key={child.id} value={child.id}>
                  {child.displayName}
                </option>
              ))}
            </select>
          </div>
          {frequency === "weekly" ? (
            <fieldset className="weekday-picker">
              <legend>Repeat on</legend>
              <div className="weekday-picker__options">
                {weekdays.map(([value, label]) => (
                  <label key={value} className="weekday-option">
                    <input type="checkbox" name="weekdays" value={value} />
                    <span>{label}</span>
                  </label>
                ))}
              </div>
            </fieldset>
          ) : null}
          <div className="form-group">
            <label htmlFor="recurringName">{frequencyLabel} job name</label>
            <input
              type="text"
              id="recurringName"
              name="name"
              required
              maxLength={160}
            />
          </div>
          <div className="form-group">
            <label htmlFor="recurringDescription">
              {frequencyLabel} job description
            </label>
            <textarea
              id="recurringDescription"
              name="description"
              rows={3}
              maxLength={1000}
            />
          </div>
          <div className="form-row">
            <div className="form-group">
              <label htmlFor="recurringPoints">
                {frequencyLabel} job points
              </label>
              <input
                type="number"
                id="recurringPoints"
                name="points"
                min="0"
                step="1"
                defaultValue={1}
                required
              />
            </div>
            <div className="form-group">
              <label htmlFor="agendaPeriod">
                {frequencyLabel} job part of day
              </label>
              <select
                id="agendaPeriod"
                name="agendaPeriod"
                defaultValue="unscheduled"
              >
                <option value="morning">Morning</option>
                <option value="arrivingHome">Arriving home</option>
                <option value="evening">Evening</option>
                <option value="unscheduled">Any time</option>
              </select>
            </div>
          </div>
          <div className="form-row">
            <div className="form-group">
              <label htmlFor="scheduledTime">
                {frequencyLabel} job time (optional)
              </label>
              <input type="time" id="scheduledTime" name="scheduledTime" />
            </div>
            <div className="form-group">
              <label htmlFor="startDate">{frequencyLabel} job starts</label>
              <input
                type="date"
                id="startDate"
                name="startDate"
                min={today}
                defaultValue={today}
                required
              />
            </div>
            <div className="form-group">
              <label htmlFor="endDate">
                {frequencyLabel} job ends (optional)
              </label>
              <input type="date" id="endDate" name="endDate" min={today} />
            </div>
          </div>

          {result?.error ? (
            <p role="alert" className="error-message">
              {result.error}
            </p>
          ) : null}
          {result?.success && !isSubmitting ? (
            <p role="status" className="success-message">
              {result.frequency === "weekly" ? "Weekly" : "Daily"} job created
              through {result.generatedThrough}.
            </p>
          ) : null}

          <button type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Creating…" : `Create ${frequency} job`}
          </button>
        </fetcher.Form>
      </div>
    </details>
  );
}
