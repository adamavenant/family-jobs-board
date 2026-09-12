import { useEffect, useRef } from "react";
import { useFetcher, useNavigate } from "react-router";

import type { TodayActionResult } from "../../app/routes";
import type { HouseholdMember } from "../../api/today";
import { ChildAssignmentPicker } from "./ChildAssignmentPicker";

export function AddJobForm({
  children,
  currentDate,
  selectedDate,
}: {
  children: HouseholdMember[];
  currentDate: string;
  selectedDate: string;
}) {
  const fetcher = useFetcher<TodayActionResult>();
  const navigate = useNavigate();
  const formRef = useRef<HTMLFormElement>(null);
  const result = fetcher.data?.intent === "add" ? fetcher.data : undefined;
  const isSubmitting = fetcher.state !== "idle";

  useEffect(() => {
    if (fetcher.state === "idle" && result?.success) {
      formRef.current?.reset();
      if (result.scheduledDate && result.scheduledDate !== selectedDate) {
        void navigate(`/?date=${result.scheduledDate}`);
      }
    }
  }, [
    fetcher.state,
    navigate,
    result?.scheduledDate,
    result?.success,
    selectedDate,
  ]);

  const defaultDate = selectedDate < currentDate ? currentDate : selectedDate;

  return (
    <details className="grown-up-tools grown-up-tools--one-off">
      <summary>
        <span className="eyebrow">Grown-up tools</span>
        <span className="grown-up-tools__action">
          Add a job <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="add-job" aria-labelledby="add-job-heading">
        <div className="add-job__heading">
          <h2 id="add-job-heading">Schedule a once-off job</h2>
          <p>Choose when it belongs on the family agenda.</p>
        </div>
        <fetcher.Form method="post" className="add-job__form" ref={formRef}>
          <input type="hidden" name="intent" value="add" />
          <ChildAssignmentPicker children={children} legend="Assign to" />
          <div className="form-group">
            <label htmlFor="name">Job name</label>
            <input type="text" id="name" name="name" required maxLength={160} />
          </div>

          <div className="form-row">
            <div className="form-group">
              <label htmlFor="scheduledDate">Scheduled date</label>
              <input
                type="date"
                id="scheduledDate"
                name="scheduledDate"
                min={currentDate}
                defaultValue={defaultDate}
                required
              />
            </div>
            <div className="form-group">
              <label htmlFor="oneOffAgendaPeriod">Part of day</label>
              <select
                id="oneOffAgendaPeriod"
                name="agendaPeriod"
                defaultValue="unscheduled"
              >
                <option value="morning">Morning</option>
                <option value="arrivingHome">Arriving home</option>
                <option value="evening">Evening</option>
                <option value="unscheduled">Any time</option>
              </select>
            </div>
            <div className="form-group">
              <label htmlFor="oneOffScheduledTime">Time (optional)</label>
              <input
                type="time"
                id="oneOffScheduledTime"
                name="scheduledTime"
              />
            </div>
          </div>

          <div className="form-group">
            <label htmlFor="description">Description</label>
            <textarea
              id="description"
              name="description"
              rows={3}
              maxLength={1000}
            />
          </div>

          <div className="form-group">
            <label htmlFor="points">Points</label>
            <input
              type="number"
              id="points"
              name="points"
              min="0"
              step="1"
              defaultValue={1}
              required
            />
          </div>

          {result?.error ? (
            <p role="alert" className="error-message" id="add-job-error">
              {result.error}
            </p>
          ) : null}

          {result?.success && !isSubmitting ? (
            <p role="status" className="success-message">
              Job scheduled for {result.scheduledDate}.
            </p>
          ) : null}

          <button type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Adding…" : "Add job"}
          </button>
        </fetcher.Form>
      </div>
    </details>
  );
}
