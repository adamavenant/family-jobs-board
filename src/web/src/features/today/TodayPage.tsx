import { useFetcher, useLoaderData } from "react-router";

import type { TodayBoard, TodayJob } from "../../api/today";
import type { CompleteActionResult } from "../../app/routes";
import { AddJobForm } from "./AddJobForm";

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
        <p className="eyebrow">Family Jobs Board</p>
        <div className="hero__content">
          <div>
            <h1>Good day, {board.child.name}!</h1>
            <p className="hero__date">{formattedDate}</p>
          </div>
          <div
            className="hero__count"
            aria-label={`${board.jobs.length} jobs today`}
          >
            <strong>{board.jobs.length}</strong>
            <span>jobs today</span>
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
  const fetcher = useFetcher<CompleteActionResult>();
  const isSubmitting = fetcher.state !== "idle";
  const error = fetcher.data?.jobId === job.id ? fetcher.data.error : undefined;
  const isPending = job.status === "pendingApproval";

  return (
    <article className={`job-card job-card--${(index % 3) + 1}`}>
      <div className="job-card__topline">
        <span className={`status status--${isPending ? "pending" : "open"}`}>
          {isPending ? "Waiting for approval" : "Ready to do"}
        </span>
        <span className="points">{job.points} pts</span>
      </div>
      <div>
        <h3>{job.name}</h3>
        <p>{job.description}</p>
      </div>

      {isPending ? (
        <div className="complete-state" role="status">
          <span aria-hidden="true">✓</span>
          Nice work — sent for approval
        </div>
      ) : (
        <fetcher.Form method="post">
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
