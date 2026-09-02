import {
  useFetcher,
  useLoaderData,
  useLocation,
  useNavigate,
} from "react-router";

import type { PointEarning, TodayBoard, TodayJob } from "../../api/today";
import type { TodayActionResult } from "../../app/routes";
import { AddJobForm } from "./AddJobForm";
import { RecurringJobForm } from "./RecurringJobForm";
import { ThemeToggle } from "../theme/ThemeToggle";

export function TodayPage() {
  const board = useLoaderData() as TodayBoard;
  const navigate = useNavigate();
  const location = useLocation();
  const children = board.members.filter((member) => !member.isAdult);
  const formattedDate = new Intl.DateTimeFormat("en", {
    weekday: "long",
    day: "numeric",
    month: "long",
  }).format(new Date(`${board.date}T12:00:00`));
  const selectMember = (memberId: string) => {
    window.localStorage.setItem("family-jobs-board-member", memberId);
    const search = new URLSearchParams(location.search);
    search.set("member", memberId);
    void navigate(`${location.pathname}?${search.toString()}`);
  };

  return (
    <main>
      <header className="hero">
        <div className="hero__toolbar">
          <p className="eyebrow">Family Jobs Board</p>
          <div className="hero__actions">
            <label className="member-picker">
              <span className="member-picker__label">Viewing as</span>
              <select
                value={board.viewer.id}
                onChange={(event) => selectMember(event.target.value)}
              >
                {board.members.map((member) => (
                  <option key={member.id} value={member.id}>
                    {member.displayName} — {member.isAdult ? "Adult" : "Child"}
                  </option>
                ))}
              </select>
            </label>
            <ThemeToggle />
          </div>
        </div>
        <div className="hero__content">
          <div>
            <h1>Good day, {board.viewer.displayName}!</h1>
            <p className="hero__date">{formattedDate}</p>
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
              aria-label={`${board.jobs.length} jobs today`}
            >
              <strong>{board.jobs.length}</strong>
              <span>jobs today</span>
            </div>
          </div>
        </div>
      </header>

      {board.viewer.isAdult ? (
        <div className="grown-up-toolbox">
          <AddJobForm children={children} />
          <RecurringJobForm
            children={children}
            viewerId={board.viewer.id}
            today={board.date}
          />
        </div>
      ) : null}

      <section className="board" aria-labelledby="today-heading">
        <div className="board__heading">
          <div>
            <p className="eyebrow">
              {board.viewer.isAdult ? "Household list" : "Your list"}
            </p>
            <h2 id="today-heading">
              {board.viewer.isAdult ? "Family jobs" : "Today’s jobs"}
            </h2>
          </div>
          <p>
            {board.viewer.isAdult
              ? "Assign jobs and review each child’s completed work."
              : "Finish a job to send it for a grown-up to approve."}
          </p>
        </div>

        <div className="job-grid">
          {board.jobs.map((job, index) => (
            <JobCard
              key={job.id}
              job={job}
              index={index}
              isAdult={board.viewer.isAdult}
            />
          ))}
        </div>
      </section>

      {!board.viewer.isAdult ? (
        <PointsHistory
          childName={board.viewer.displayName}
          earnings={board.pointEarnings}
        />
      ) : null}
    </main>
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
        <p>Approved jobs appear here, newest first.</p>
      </div>

      {earnings.length === 0 ? (
        <p className="points-history__empty">
          No points earned yet. Complete and approve a job to start the list.
        </p>
      ) : (
        <ol className="earning-list">
          {earnings.map((earning) => (
            <li key={earning.id}>
              <span className="earning-list__points">+{earning.points}</span>
              <span>
                <strong>{earning.jobName}</strong>
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
        <span
          className={`status status--${isApproved ? "approved" : isPending ? "pending" : "open"}`}
        >
          {isApproved
            ? "Points awarded"
            : isPending
              ? "Waiting for approval"
              : "Ready to do"}
        </span>
        <span className="points">{job.points} pts</span>
      </div>
      <div>
        {isAdult ? (
          <p className="job-card__assignee">For {job.childDisplayName}</p>
        ) : null}
        <h3>{job.name}</h3>
        {job.recurringJobSeriesId ? (
          <p className="job-card__schedule">
            Daily · {formatAgendaPeriod(job.agendaPeriod)}
            {job.scheduledTime ? ` · ${job.scheduledTime.slice(0, 5)}` : ""}
          </p>
        ) : null}
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

function formatAgendaPeriod(value: TodayJob["agendaPeriod"]) {
  return value === "arrivingHome"
    ? "Arriving home"
    : value === "unscheduled"
      ? "Any time"
      : `${value.charAt(0).toUpperCase()}${value.slice(1)}`;
}
