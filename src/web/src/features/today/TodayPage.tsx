import { useFetcher, useLoaderData } from "react-router";

import type { TodayBoard, TodayJob } from "../../api/today";
import type { TodayActionResult } from "../../app/routes";
import { AddJobForm } from "./AddJobForm";
import { ThemeToggle } from "../theme/ThemeToggle";

export function TodayPage() {
  const board = useLoaderData() as TodayBoard;
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
          <ThemeToggle />
        </div>
        <div className="hero__content">
          <div>
            <h1>Good day, {board.child.name}!</h1>
            <p className="hero__date">{formattedDate}</p>
          </div>
          <div className="hero__stats">
            <div
              className="hero__balance"
              aria-label={`${board.child.pointsBalance} points earned`}
            >
              <strong>{board.child.pointsBalance}</strong>
              <span>points earned</span>
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

      <AddJobForm />

      <section className="board" aria-labelledby="today-heading">
        <div className="board__heading">
          <div>
            <p className="eyebrow">Your list</p>
            <h2 id="today-heading">Today’s jobs</h2>
          </div>
          <p>Finish a job to send it for a grown-up to approve.</p>
        </div>

        <div className="job-grid">
          {board.jobs.map((job, index) => (
            <JobCard key={job.id} job={job} index={index} />
          ))}
        </div>
      </section>
    </main>
  );
}

function JobCard({ job, index }: { job: TodayJob; index: number }) {
  const fetcher = useFetcher<TodayActionResult>();
  const isSubmitting = fetcher.state !== "idle";
  const error =
    (fetcher.data?.intent === "complete" ||
      fetcher.data?.intent === "approve") &&
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
        <h3>{job.name}</h3>
        <p>{job.description}</p>
      </div>

      {isApproved ? (
        <div className="complete-state complete-state--approved" role="status">
          <span aria-hidden="true">★</span>
          Approved — {job.points} points awarded
        </div>
      ) : isPending ? (
        <fetcher.Form method="post" className="approval-form">
          <input type="hidden" name="intent" value="approve" />
          <input type="hidden" name="jobId" value={job.id} />
          <p>Nice work — ready for a grown-up.</p>
          <button type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Approving…" : `Approve +${job.points} points`}
          </button>
        </fetcher.Form>
      ) : (
        <fetcher.Form method="post">
          <input type="hidden" name="intent" value="complete" />
          <input type="hidden" name="jobId" value={job.id} />
          <button type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Sending…" : "Mark as done"}
          </button>
        </fetcher.Form>
      )}

      {error ? (
        <p role="alert" className="error-message">
          {error}
        </p>
      ) : null}
    </article>
  );
}
