import { useEffect, useRef } from "react";
import { useFetcher } from "react-router";

import type { TodayActionResult } from "../../app/routes";

export function AddJobForm() {
  const fetcher = useFetcher<TodayActionResult>();
  const formRef = useRef<HTMLFormElement>(null);
  const result = fetcher.data?.intent === "add" ? fetcher.data : undefined;
  const isSubmitting = fetcher.state !== "idle";

  useEffect(() => {
    if (fetcher.state === "idle" && result?.success) {
      formRef.current?.reset();
    }
  }, [fetcher.state, result?.success]);

  return (
    <section className="add-job" aria-labelledby="add-job-heading">
      <div className="add-job__heading">
        <p className="eyebrow">Grown-up tools</p>
        <h2 id="add-job-heading">Add today’s job</h2>
        <p>Create another job for this board.</p>
      </div>
      <fetcher.Form method="post" className="add-job__form" ref={formRef}>
        <input type="hidden" name="intent" value="add" />
        <div className="form-group">
          <label htmlFor="name">Job name</label>
          <input type="text" id="name" name="name" required maxLength={160} />
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
            Job added to today’s board.
          </p>
        ) : null}

        <button type="submit" disabled={isSubmitting}>
          {isSubmitting ? "Adding…" : "Add job"}
        </button>
      </fetcher.Form>
    </section>
  );
}
