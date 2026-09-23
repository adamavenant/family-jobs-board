import { Link, useFetcher } from "react-router";

import type {
  HouseholdMember,
  PointEarning,
  TodayBoard,
  TodayJob,
} from "../../api/today";
import type { AppActionResult, TodayActionResult } from "../../app/routes";
import { AddJobForm } from "./AddJobForm";
import { RecurringJobForm } from "./RecurringJobForm";
import { ThemeToggle } from "../theme/ThemeToggle";
import { FamilyMembersPanel } from "../identity/FamilyMembersPanel";
import { AdminDataResetPanel } from "../administration/AdminDataResetPanel";
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

export function TodayPage({ board }: { board: TodayBoard }) {
  const children = board.members.filter((member) => !member.isAdult);
  const currentDate = board.currentDate ?? board.date;
  const selectedChild = children.find(
    (child) => child.id === board.selectedChildId,
  );
  const isToday = board.date === currentDate;
  const formattedDate = new Intl.DateTimeFormat("en", {
    weekday: "long",
    day: "numeric",
    month: "long",
  }).format(new Date(`${board.date}T12:00:00`));
  return (
    <main>
      <header className="hero">
        <div className="hero__toolbar">
          <p className="eyebrow">Family Jobs Board</p>
          <div className="hero__actions">
            {board.viewer.isAdult ? (
              <Link className="calendar-link" to="/calendar">
                Calendar
              </Link>
            ) : null}
            <IdentityControls viewer={board.viewer} />
            <ThemeToggle />
          </div>
        </div>
        <div className="hero__content">
          <div>
            <h1>Good day, {board.viewer.displayName}!</h1>
            <p className="hero__date">
              {formattedDate}
              {isToday ? <span>Today</span> : null}
            </p>
          </div>
          <div className="hero__stats">
            <div
              className="hero__balance"
              aria-label={
                board.viewer.isAdult
                  ? `${board.pendingApprovalCount} jobs awaiting review`
                  : `${board.pointsBalance ?? 0} points earned`
              }
            >
              <strong>
                {board.viewer.isAdult
                  ? board.pendingApprovalCount
                  : (board.pointsBalance ?? 0)}
              </strong>
              <span>
                {board.viewer.isAdult ? "awaiting review" : "points earned"}
              </span>
            </div>
            <div
              className="hero__count"
              aria-label={`${board.jobs.length} jobs on this day`}
            >
              <strong>{board.jobs.length}</strong>
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
          <Link
            to={boardHref(
              addDays(board.date, -1),
              currentDate,
              board.selectedChildId,
            )}
          >
            ← Previous day
          </Link>
          {!isToday ? (
            <Link
              to={boardHref(currentDate, currentDate, board.selectedChildId)}
            >
              Today
            </Link>
          ) : (
            <span aria-current="date">Today</span>
          )}
          <Link
            to={boardHref(
              addDays(board.date, 1),
              currentDate,
              board.selectedChildId,
            )}
          >
            Next day →
          </Link>
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

        {board.jobs.length === 0 ? (
          <p className="board__empty">
            {selectedChild
              ? `No jobs are scheduled for ${selectedChild.displayName} on this day.`
              : "No jobs are scheduled for this day."}
          </p>
        ) : (
          <Agenda jobs={board.jobs} isAdult={board.viewer.isAdult} />
        )}
      </section>

      {!board.viewer.isAdult ? (
        <>
          <div className="grown-up-toolbox">
            <GoodBehaviourTypesList />
          </div>
          <PointsHistory
            childName={board.viewer.displayName}
            earnings={board.pointEarnings}
          />
        </>
      ) : null}
    </main>
  );
}

const agendaPeriods = [
  ["morning", "Morning"],
  ["arrivingHome", "Arriving home"],
  ["evening", "Evening"],
  ["unscheduled", "Any time"],
] as const;

function Agenda({ jobs, isAdult }: { jobs: TodayJob[]; isAdult: boolean }) {
  return (
    <div className="agenda">
      {agendaPeriods.map(([period, label]) => {
        const periodJobs = jobs.filter((job) => job.agendaPeriod === period);
        if (periodJobs.length === 0) {
          return null;
        }

        return (
          <section className="agenda-period" key={period}>
            <h3>{label}</h3>
            <div className="job-grid">
              {periodJobs.map((job) => {
                return (
                  <JobCard
                    key={job.id}
                    job={job}
                    index={jobs.indexOf(job)}
                    isAdult={isAdult}
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

export function IdentityControls({ viewer }: { viewer: HouseholdMember }) {
  const fetcher = useFetcher<AppActionResult>();
  const submitting = fetcher.state !== "idle";

  return (
    <details className="identity-menu">
      <summary>{viewer.displayName}</summary>
      <div className="identity-menu__panel">
        <p>
          Signed in as <strong>{viewer.displayName}</strong>
        </p>
        <fetcher.Form method="post">
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

function PointsHistory({
  childName,
  earnings,
}: {
  childName: string;
  earnings: PointEarning[];
}) {
  return (
    <section
      className="points-history"
      aria-labelledby="points-history-heading"
    >
      <div className="points-history__heading">
        <div>
          <p className="eyebrow">Points</p>
          <h2 id="points-history-heading">How {childName} earned them</h2>
        </div>
        <p>
          Approved jobs, good behaviours, and adjustments appear here, newest
          first.
        </p>
      </div>

      {earnings.length === 0 ? (
        <p className="points-history__empty">
          No points earned yet. Complete and approve a job, or show a good
          behaviour, to start the list.
        </p>
      ) : (
        <ol className="earning-list">
          {earnings.map((earning) => (
            <li key={earning.id}>
              <span
                className={
                  earning.points < 0
                    ? "earning-list__points earning-list__points--negative"
                    : "earning-list__points"
                }
              >
                {formatPoints(earning.points)}
              </span>
              <span>
                <strong>{earning.name}</strong>
                <span className="earning-list__source">
                  {earningSourceLabel(earning)}
                </span>
                <time dateTime={earning.awardedAtUtc}>
                  {formatAwardTime(earning.awardedAtUtc)}
                </time>
              </span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function formatPoints(points: number): string {
  return points < 0 ? `−${Math.abs(points)}` : `+${points}`;
}

function earningSourceLabel(earning: PointEarning): string {
  const adult = earning.loggedByDisplayName;
  if (earning.source === "goodBehaviour") {
    return adult ? `Good behaviour · logged by ${adult}` : "Good behaviour";
  }
  if (earning.source === "manualAdjustment") {
    return adult ? `Adjustment · by ${adult}` : "Adjustment";
  }

  return "Job";
}

function formatAwardTime(value: string) {
  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function JobCard({
  job,
  index,
  isAdult,
}: {
  job: TodayJob;
  index: number;
  isAdult: boolean;
}) {
  const fetcher = useFetcher<TodayActionResult>();
  const isSubmitting = fetcher.state !== "idle";
  const submittingIntent = fetcher.formData?.get("intent");
  const error =
    (fetcher.data?.intent === "complete" ||
      fetcher.data?.intent === "approve" ||
      fetcher.data?.intent === "reject") &&
    fetcher.data.jobId === job.id
      ? fetcher.data.error
      : undefined;
  const isPending = job.status === "pendingApproval";
  const isApproved = job.status === "approved";

  return (
    <article className={`job-card job-card--${(index % 3) + 1}`}>
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
        <h3>{job.name}</h3>
        <p className="job-card__schedule">
          {job.recurringJobSeriesId
            ? `${formatRecurrenceFrequency(job.recurrenceFrequency)} · `
            : ""}
          {formatAgendaPeriod(job.agendaPeriod)}
          {job.scheduledTime ? ` · ${job.scheduledTime.slice(0, 5)}` : ""}
        </p>
        <p>{job.description}</p>
      </div>

      {isApproved ? (
        <div className="complete-state complete-state--approved" role="status">
          <span aria-hidden="true">★</span>
          Approved — {job.points} points awarded
        </div>
      ) : isPending && isAdult ? (
        <fetcher.Form method="post" className="approval-form">
          <input type="hidden" name="jobId" value={job.id} />
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
        <div className="complete-state" role="status">
          Ready for {job.childDisplayName}
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
