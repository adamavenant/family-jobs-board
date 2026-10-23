import { useEffect, useRef, useState } from "react";
import { Link, useFetcher } from "react-router";

import type { HouseholdMember, TodayBoard, TodayJob } from "../../api/today";
import type { AppActionResult, TodayActionResult } from "../../app/routes";
import { useUndoQueue } from "../../app/undoQueue";
import { AddJobForm } from "./AddJobForm";
import { FloatingDateBar } from "./FloatingDateBar";
import { RecurringJobForm } from "./RecurringJobForm";
import { ThemeToggle } from "../theme/ThemeToggle";
import { FamilyMembersPanel } from "../identity/FamilyMembersPanel";
import { AdminDataResetPanel } from "../administration/AdminDataResetPanel";
import { HouseholdSettingsPanel } from "../settings/HouseholdSettingsPanel";
import { ApprovalsReview, ApprovalsTrigger } from "../approvals/ApprovalsPanel";
import {
  addDays,
  boardHref,
  formatAgendaPeriod,
  formatRecurrenceFrequency,
  jobStatusClassName,
  jobStatusLabel,
} from "./jobFormatting";
import { GoodBehavioursPanel } from "../goodBehaviours/GoodBehavioursPanel";
import { GoodBehaviourTypesList } from "../goodBehaviours/GoodBehaviourTypesList";
import { PointAdjustmentsPanel } from "../pointAdjustments/PointAdjustmentsPanel";
import { RedeemRewardPanel } from "../pointRedemptions/RedeemRewardPanel";
import { WhoseTurnCard } from "../whoseTurn/WhoseTurnCard";
import { WhoseTurnPanel } from "../whoseTurn/WhoseTurnPanel";
import { formatBalance } from "../points/pointsFormatting";
import {
  ScopedRecurringJobCancelForm,
  ScopedRecurringJobEditForm,
} from "./ScopedRecurringJobForms";
import { JobNameFields } from "./JobNameFields";
import { ReverseApprovalControl } from "./ReverseApprovalControl";
import { showQueuedJobChanges } from "./queuedJobChanges";
import type { ShownJob } from "./queuedJobChanges";

export function TodayPage({ board }: { board: TodayBoard }) {
  const children = board.members.filter((member) => !member.isAdult);
  const currentDate = board.currentDate ?? board.date;
  const selectedChild = children.find(
    (child) => child.id === board.selectedChildId,
  );
  const isToday = board.date === currentDate;
  const dateLineRef = useRef<HTMLParagraphElement>(null);
  // Queued actions already show as done; they are saved after the undo time.
  const { actions } = useUndoQueue();
  const { jobs, pendingApprovalCount } = showQueuedJobChanges(board, actions);
  const formattedDate = new Intl.DateTimeFormat("en", {
    weekday: "long",
    day: "numeric",
    month: "long",
  }).format(new Date(`${board.date}T12:00:00`));
  const previousDayHref = boardHref(
    addDays(board.date, -1),
    currentDate,
    board.selectedChildId,
  );
  const nextDayHref = boardHref(
    addDays(board.date, 1),
    currentDate,
    board.selectedChildId,
  );
  return (
    <ApprovalsReview currentDate={currentDate}>
      <main>
        <FloatingDateBar
          dateLineRef={dateLineRef}
          date={board.date}
          isToday={isToday}
          todayHref={boardHref(currentDate, currentDate, board.selectedChildId)}
          previousHref={previousDayHref}
          nextHref={nextDayHref}
          pendingApprovalCount={
            board.viewer.isAdult ? pendingApprovalCount : null
          }
        />
        <header className="hero">
          <div className="hero__toolbar">
            <p className="eyebrow">Family Jobs Board</p>
            <div className="hero__actions">
              {board.viewer.isAdult ? (
                <>
                  <Link className="calendar-link" to="/calendar">
                    Calendar
                  </Link>
                  <Link className="points-link" to="/points">
                    Points
                  </Link>
                </>
              ) : null}
              <IdentityControls viewer={board.viewer} />
              <ThemeToggle />
            </div>
          </div>
          <div className="hero__content">
            <div>
              <h1>Good day, {board.viewer.displayName}!</h1>
              <p className="hero__date" ref={dateLineRef}>
                {formattedDate}
                {isToday ? <span>Today</span> : null}
              </p>
            </div>
            <div className="hero__stats">
              {board.viewer.isAdult ? (
                <ApprovalsTrigger count={pendingApprovalCount} />
              ) : (
                <Link
                  className="hero__balance"
                  to="/points"
                  aria-label={`${formatBalance(board.pointsBalance ?? 0)} points earned. See how you earned them`}
                >
                  <strong>{formatBalance(board.pointsBalance ?? 0)}</strong>
                  <span>points earned</span>
                </Link>
              )}
              <div
                className="hero__count"
                aria-label={`${jobs.length} jobs on this day`}
              >
                <strong>{jobs.length}</strong>
                <span>jobs this day</span>
              </div>
            </div>
          </div>
        </header>

        {board.viewer.isAdult ? (
          <div className="grown-up-toolbox">
            <FamilyMembersPanel currentMemberId={board.viewer.id} />
            <AddJobForm
              key={board.date}
              children={children}
              currentDate={currentDate}
              selectedDate={board.date}
              selectedChildId={board.selectedChildId}
            />
            <RecurringJobForm children={children} today={currentDate} />
            <GoodBehavioursPanel children={children} />
            <PointAdjustmentsPanel children={children} />
            <RedeemRewardPanel children={children} />
            <WhoseTurnPanel children={children} currentDate={currentDate} />
            <HouseholdSettingsPanel />
            <AdminDataResetPanel />
          </div>
        ) : null}

        <section className="board" aria-labelledby="today-heading">
          {board.viewer.isAdult ? (
            <nav className="child-filter" aria-label="Filter agenda by child">
              <span>Show jobs for</span>
              <div>
                <Link
                  to={boardHref(board.date, currentDate, null)}
                  aria-current={
                    board.selectedChildId === null ? "page" : undefined
                  }
                >
                  All children
                </Link>
                {children.map((child) => (
                  <Link
                    key={child.id}
                    to={boardHref(board.date, currentDate, child.id)}
                    aria-current={
                      board.selectedChildId === child.id ? "page" : undefined
                    }
                  >
                    {child.displayName}
                  </Link>
                ))}
              </div>
            </nav>
          ) : null}
          <nav className="date-navigation" aria-label="Daily agenda">
            <Link to={previousDayHref}>← Previous day</Link>
            {!isToday ? (
              <Link
                to={boardHref(currentDate, currentDate, board.selectedChildId)}
              >
                Today
              </Link>
            ) : (
              <span aria-current="date">Today</span>
            )}
            <Link to={nextDayHref}>Next day →</Link>
          </nav>
          <div className="board__heading">
            <div>
              <p className="eyebrow">
                {board.viewer.isAdult ? "Household list" : "Your list"}
              </p>
              <h2 id="today-heading">
                {board.viewer.isAdult
                  ? selectedChild
                    ? `${selectedChild.displayName}’s jobs`
                    : isToday
                      ? "Family jobs"
                      : "Family agenda"
                  : isToday
                    ? "Today’s jobs"
                    : "Your jobs"}
              </h2>
            </div>
            <p>
              {board.viewer.isAdult
                ? "Assign jobs and review each child’s completed work."
                : "Finish a job to send it for a grown-up to approve."}
            </p>
          </div>

          {board.whoseTurns.map((turn) => (
            <WhoseTurnCard
              key={turn.rotationId}
              whoseTurn={turn}
              date={board.date}
              isToday={isToday}
            />
          ))}
          {board.whoseTurns.length === 0 && board.viewer.isAdult ? (
            <p className="whose-turn-card whose-turn-card--unset">
              Whose Turn Is It? isn’t set up yet. Configure it in the tools
              above.
            </p>
          ) : null}

          {jobs.length === 0 ? (
            <p className="board__empty">
              {selectedChild
                ? `No jobs are scheduled for ${selectedChild.displayName} on this day.`
                : "No jobs are scheduled for this day."}
            </p>
          ) : (
            <Agenda jobs={jobs} isAdult={board.viewer.isAdult} />
          )}
        </section>

        {!board.viewer.isAdult ? (
          <div className="grown-up-toolbox">
            <GoodBehaviourTypesList />
          </div>
        ) : null}
      </main>
    </ApprovalsReview>
  );
}

const agendaPeriods = [
  ["morning", "Morning"],
  ["arrivingHome", "Arriving home"],
  ["evening", "Evening"],
  ["unscheduled", "Any time"],
] as const;

function Agenda({ jobs, isAdult }: { jobs: ShownJob[]; isAdult: boolean }) {
  return (
    <div className="agenda">
      {agendaPeriods.map(([period, label]) => {
        const periodJobs = jobs.filter(
          (shown) => shown.job.agendaPeriod === period,
        );
        if (periodJobs.length === 0) {
          return null;
        }

        return (
          <section className="agenda-period" key={period}>
            <h3>{label}</h3>
            <div className="job-grid">
              {periodJobs.map((shown) => {
                return shown.adding ? (
                  <AddingJobCard
                    key={shown.job.id}
                    job={shown.job}
                    index={jobs.indexOf(shown)}
                  />
                ) : (
                  <JobCard
                    key={shown.job.id}
                    job={shown.job}
                    index={jobs.indexOf(shown)}
                    isAdult={isAdult}
                    queued={shown.queued}
                  />
                );
              })}
            </div>
          </section>
        );
      })}
    </div>
  );
}

/** A job that waits behind an undo toast before the server creates it. */
function AddingJobCard({ job, index }: { job: TodayJob; index: number }) {
  return (
    <article
      className={`job-card job-card--${(index % 3) + 1} job-card--queued`}
      aria-label={`${job.name} for ${job.childDisplayName}, being added`}
    >
      <div className="job-card__topline">
        <span className="status status--open">Being added</span>
        <span className="points">{job.points} pts</span>
      </div>
      <div>
        <p className="job-card__assignee">For {job.childDisplayName}</p>
        <h3>{job.name}</h3>
        <p className="job-card__schedule">
          {formatAgendaPeriod(job.agendaPeriod)}
          {job.scheduledTime ? ` · ${job.scheduledTime.slice(0, 5)}` : ""}
        </p>
        <p>{job.description}</p>
      </div>
    </article>
  );
}

export function IdentityControls({
  viewer,
}: {
  viewer: Pick<HouseholdMember, "displayName">;
}) {
  const fetcher = useFetcher<AppActionResult>();
  const submitting = fetcher.state !== "idle";

  return (
    <details className="identity-menu">
      <summary>{viewer.displayName}</summary>
      <div className="identity-menu__panel">
        <p>
          Signed in as <strong>{viewer.displayName}</strong>
        </p>
        {/* Sign out through the board's action, so it works from every page. */}
        <fetcher.Form method="post" action="/">
          <button
            type="submit"
            name="intent"
            value="logout"
            className="button--quiet"
            disabled={submitting}
          >
            Sign out
          </button>
        </fetcher.Form>
      </div>
    </details>
  );
}

function JobCard({
  job,
  index,
  isAdult,
  queued,
}: {
  job: TodayJob;
  index: number;
  isAdult: boolean;
  queued: boolean;
}) {
  const fetcher = useFetcher<TodayActionResult>();
  const [confirmingAdultCompletion, setConfirmingAdultCompletion] =
    useState(false);
  const confirmationKey = `${job.status}:${job.points}`;
  const [confirmationFor, setConfirmationFor] = useState(confirmationKey);
  const completionTriggerRef = useRef<HTMLButtonElement>(null);
  const restoreCompletionFocus = useRef(false);
  if (confirmationFor !== confirmationKey) {
    setConfirmationFor(confirmationKey);
    setConfirmingAdultCompletion(false);
  }

  useEffect(() => {
    if (!confirmingAdultCompletion && restoreCompletionFocus.current) {
      completionTriggerRef.current?.focus();
      restoreCompletionFocus.current = false;
    }
  }, [confirmingAdultCompletion]);
  // A reversal swaps the card's controls, so keep the reader on the card they acted on.
  const headingRef = useRef<HTMLHeadingElement>(null);
  const focusedReversal = useRef<TodayActionResult | null>(null);
  useEffect(() => {
    const result = fetcher.data;
    if (
      fetcher.state === "idle" &&
      result?.intent === "reverseApproval" &&
      result.jobId === job.id &&
      !result.error &&
      job.status !== "approved" &&
      focusedReversal.current !== result
    ) {
      focusedReversal.current = result;
      headingRef.current?.focus();
    }
  }, [fetcher.state, fetcher.data, job.id, job.status]);
  // While a queued action waits to be saved, the job can't be acted on again.
  const isSubmitting = fetcher.state !== "idle" || queued;
  const submittingIntent = fetcher.formData?.get("intent");
  const error =
    (fetcher.data?.intent === "complete" ||
      fetcher.data?.intent === "approve" ||
      fetcher.data?.intent === "reject" ||
      fetcher.data?.intent === "editJob" ||
      fetcher.data?.intent === "cancelJob" ||
      fetcher.data?.intent === "reverseApproval") &&
    fetcher.data.jobId === job.id
      ? fetcher.data.error
      : undefined;
  const isPending = job.status === "pendingApproval";
  const isApproved = job.status === "approved";

  return (
    <article
      className={`job-card job-card--${(index % 3) + 1}${queued ? " job-card--queued" : ""}`}
    >
      <div className="job-card__topline">
        <span className={jobStatusClassName(job.status)}>
          {jobStatusLabel(job.status)}
        </span>
        <span className="points">{job.points} pts</span>
      </div>
      <div>
        {isAdult ? (
          <p className="job-card__assignee">For {job.childDisplayName}</p>
        ) : null}
        <h3 ref={headingRef} tabIndex={-1}>
          {job.name}
        </h3>
        <p className="job-card__schedule">
          {job.recurringJobSeriesId
            ? `${formatRecurrenceFrequency(job.recurrenceFrequency)} · `
            : ""}
          {formatAgendaPeriod(job.agendaPeriod)}
          {job.scheduledTime ? ` · ${job.scheduledTime.slice(0, 5)}` : ""}
        </p>
        <p>{job.description}</p>
      </div>

      {isAdult && !isApproved && !queued ? (
        <div className="job-management">
          {job.recurringJobSeriesId ? (
            <ScopedRecurringJobEditForm job={job} />
          ) : (
            <details>
              <summary>Edit job</summary>
              <fetcher.Form method="post" className="job-management__form">
                <input type="hidden" name="intent" value="editJob" />
                <input type="hidden" name="jobId" value={job.id} />
                <label htmlFor={`edit-name-${job.id}`}>Edit job name</label>
                <input
                  id={`edit-name-${job.id}`}
                  name="name"
                  defaultValue={job.name}
                  maxLength={160}
                  required
                />
                <label htmlFor={`edit-description-${job.id}`}>
                  Edit description
                </label>
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
                    Edit scheduled date
                    <input
                      id={`edit-date-${job.id}`}
                      name="scheduledDate"
                      type="date"
                      defaultValue={job.scheduledDate}
                      required
                    />
                  </label>
                </div>
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
                <button type="submit" disabled={isSubmitting}>
                  {isSubmitting && submittingIntent === "editJob"
                    ? "Saving…"
                    : "Save changes"}
                </button>
              </fetcher.Form>
            </details>
          )}
          {job.recurringJobSeriesId ? (
            <ScopedRecurringJobCancelForm job={job} />
          ) : (
            <details>
              <summary>Cancel job</summary>
              <fetcher.Form method="post" className="job-management__form">
                <input type="hidden" name="intent" value="cancelJob" />
                <input type="hidden" name="jobId" value={job.id} />
                <JobNameFields job={job} />
                <label htmlFor={`cancel-reason-${job.id}`}>
                  Cancellation reason <span>(optional)</span>
                </label>
                <textarea
                  id={`cancel-reason-${job.id}`}
                  name="reason"
                  maxLength={500}
                  rows={2}
                />
                <p>Cancelling is permanent and awards no points.</p>
                <button
                  type="submit"
                  className="button--danger"
                  disabled={isSubmitting}
                >
                  {isSubmitting && submittingIntent === "cancelJob"
                    ? "Cancelling…"
                    : "Cancel this job"}
                </button>
              </fetcher.Form>
            </details>
          )}
        </div>
      ) : null}

      {isApproved ? (
        <>
          <div
            className="complete-state complete-state--approved"
            role="status"
          >
            <span aria-hidden="true">★</span>
            Approved — {job.points} points awarded
          </div>
          {isAdult && !queued ? (
            <ReverseApprovalControl job={job} fetcher={fetcher} />
          ) : null}
        </>
      ) : isPending && isAdult ? (
        <fetcher.Form method="post" className="approval-form">
          <input type="hidden" name="jobId" value={job.id} />
          <input type="hidden" name="expectedPoints" value={job.points} />
          <JobNameFields job={job} />
          <p>Nice work — ready for a grown-up.</p>
          <label htmlFor={`rejection-reason-${job.id}`}>
            Rejection reason <span>(optional)</span>
          </label>
          <textarea
            id={`rejection-reason-${job.id}`}
            name="reason"
            maxLength={500}
            rows={2}
          />
          <div className="review-actions">
            <button
              type="submit"
              name="intent"
              value="reject"
              className="button--secondary"
              disabled={isSubmitting}
            >
              {isSubmitting && submittingIntent === "reject"
                ? "Rejecting…"
                : "Reject job"}
            </button>
            <button
              type="submit"
              name="intent"
              value="approve"
              disabled={isSubmitting}
            >
              {isSubmitting && submittingIntent === "approve"
                ? "Approving…"
                : `Approve +${job.points} points`}
            </button>
          </div>
        </fetcher.Form>
      ) : isPending ? (
        <div className="complete-state" role="status">
          Sent to a grown-up for approval
        </div>
      ) : isAdult ? (
        <div className="open-job-actions">
          {confirmingAdultCompletion ? (
            <div
              className="inline-confirmation"
              role="group"
              aria-label={`Confirm completion for ${job.childDisplayName}`}
            >
              <p>
                Mark done and award {job.points}{" "}
                {job.points === 1 ? "point" : "points"} to{" "}
                {job.childDisplayName}?
              </p>
              <div>
                <fetcher.Form method="post">
                  <input type="hidden" name="intent" value="complete" />
                  <input type="hidden" name="jobId" value={job.id} />
                  <input
                    type="hidden"
                    name="expectedPoints"
                    value={job.points}
                  />
                  <JobNameFields job={job} />
                  <button type="submit" disabled={isSubmitting}>
                    {isSubmitting && submittingIntent === "complete"
                      ? "Awarding…"
                      : "Yes, mark done"}
                  </button>
                </fetcher.Form>
                <button
                  type="button"
                  className="button--quiet"
                  autoFocus
                  onClick={() => {
                    restoreCompletionFocus.current = true;
                    setConfirmingAdultCompletion(false);
                  }}
                  disabled={isSubmitting}
                >
                  Keep open
                </button>
              </div>
            </div>
          ) : (
            <button
              ref={completionTriggerRef}
              type="button"
              onClick={() => setConfirmingAdultCompletion(true)}
              disabled={queued}
            >
              Mark as done for {job.childDisplayName}
            </button>
          )}
        </div>
      ) : (
        <div className="open-job-actions">
          {job.latestRejection ? (
            <div className="rejection-feedback" role="status">
              <strong>Needs another go</strong>
              <p>
                {job.latestRejection.reason ??
                  "A grown-up asked you to try this job again."}
              </p>
            </div>
          ) : null}
          <fetcher.Form method="post">
            <input type="hidden" name="intent" value="complete" />
            <input type="hidden" name="jobId" value={job.id} />
            <JobNameFields job={job} />
            <button type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Sending…" : "Mark as done"}
            </button>
          </fetcher.Form>
        </div>
      )}

      {error ? (
        <p role="alert" className="error-message">
          {error}
        </p>
      ) : null}
    </article>
  );
}
