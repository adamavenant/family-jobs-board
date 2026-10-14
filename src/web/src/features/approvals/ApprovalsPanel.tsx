import {
  createContext,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
} from "react";
import type { ReactNode } from "react";
import { useFetcher } from "react-router";

import type { TodayJob } from "../../api/today";
import { addDays, formatAgendaPeriod } from "../today/jobFormatting";
import { ApprovalControls } from "./ApprovalControls";
import type { ApprovalsLoaderData } from "./approvalsRoute";

interface ApprovalsReviewValue {
  isOpen: boolean;
  panelId: string;
  toggle: (opener: HTMLElement) => void;
}

const ApprovalsReviewContext = createContext<ApprovalsReviewValue | null>(null);

/**
 * Owns the review side panel for a page, so every "awaiting review" trigger on it (the header
 * badge, and any pinned copy) opens the same panel. The panel lists every pending job across
 * all dates and is non-modal, so grown-ups can keep browsing while they review.
 */
export function ApprovalsReview({
  currentDate,
  children,
}: {
  currentDate: string;
  children: ReactNode;
}) {
  const fetcher = useFetcher<ApprovalsLoaderData>();
  const [isOpen, setIsOpen] = useState(false);
  const openerRef = useRef<HTMLElement | null>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const panelId = useId();
  const headingId = useId();
  const jobs =
    fetcher.data && "jobs" in fetcher.data ? fetcher.data.jobs : undefined;
  const loadError =
    fetcher.data && "error" in fetcher.data ? fetcher.data.error : undefined;
  const jobCount = jobs?.length;

  useEffect(() => {
    if (isOpen) {
      headingRef.current?.focus();
    }
  }, [isOpen]);

  useEffect(() => {
    // A decided job leaves the list while its button has focus; keep focus inside the panel.
    if (
      isOpen &&
      (document.activeElement === null ||
        document.activeElement === document.body)
    ) {
      headingRef.current?.focus();
    }
  }, [isOpen, jobCount]);

  function close() {
    setIsOpen(false);
    // Return focus to whichever trigger opened the panel, or to another one if it has gone.
    const opener = openerRef.current?.isConnected
      ? openerRef.current
      : document.querySelector<HTMLElement>("[data-approvals-trigger]");
    opener?.focus();
  }

  const value: ApprovalsReviewValue = {
    isOpen,
    panelId,
    toggle: (opener) => {
      if (isOpen) {
        close();
        return;
      }
      openerRef.current = opener;
      setIsOpen(true);
      void fetcher.load("/approvals");
    },
  };

  return (
    <ApprovalsReviewContext.Provider value={value}>
      {children}
      {isOpen ? (
        <aside
          id={panelId}
          className="approvals-panel"
          aria-labelledby={headingId}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              event.stopPropagation();
              close();
            }
          }}
        >
          <div className="approvals-panel__header">
            <div>
              <p className="eyebrow">Review queue</p>
              <h2 id={headingId} ref={headingRef} tabIndex={-1}>
                Awaiting review
              </h2>
            </div>
            <button
              type="button"
              className="approvals-panel__close"
              aria-label="Close review panel"
              onClick={close}
            >
              ×
            </button>
          </div>

          {loadError ? (
            <div className="approvals-panel__error">
              <p role="alert" className="error-message">
                {loadError}
              </p>
              <button
                type="button"
                className="button--quiet"
                onClick={() => void fetcher.load("/approvals")}
              >
                Try again
              </button>
            </div>
          ) : jobs === undefined ? (
            <p role="status" className="approvals-panel__status">
              Loading jobs awaiting review…
            </p>
          ) : jobs.length === 0 ? (
            <p role="status" className="approvals-panel__status">
              Nothing is waiting for review. Nice work, everyone!
            </p>
          ) : (
            groupByDate(jobs).map(([date, dateJobs]) => (
              <section key={date} className="approvals-panel__day">
                <h3
                  className={
                    date < currentDate
                      ? "approvals-panel__date approvals-panel__date--earlier"
                      : "approvals-panel__date"
                  }
                >
                  {reviewDateLabel(date, currentDate)}
                </h3>
                <ul>
                  {dateJobs.map((job) => (
                    <li key={job.id} className="approvals-panel__job">
                      <div className="approvals-panel__job-summary">
                        <strong>{job.name}</strong>
                        <span>
                          For {job.childDisplayName} ·{" "}
                          {formatAgendaPeriod(job.agendaPeriod)}
                          {job.scheduledTime
                            ? ` · ${job.scheduledTime.slice(0, 5)}`
                            : ""}
                        </span>
                      </div>
                      <ApprovalControls job={job} />
                    </li>
                  ))}
                </ul>
              </section>
            ))
          )}
        </aside>
      ) : null}
    </ApprovalsReviewContext.Provider>
  );
}

/** An "awaiting review" button that opens or closes the page's review panel. */
export function ApprovalsTrigger({
  count,
  className = "hero__balance approvals-trigger",
}: {
  count: number;
  className?: string;
}) {
  const review = useContext(ApprovalsReviewContext);
  if (!review) {
    throw new Error("ApprovalsTrigger must be used within ApprovalsReview.");
  }

  return (
    <button
      type="button"
      className={className}
      data-approvals-trigger=""
      aria-expanded={review.isOpen}
      aria-controls={review.isOpen ? review.panelId : undefined}
      aria-label={`${count} jobs awaiting review`}
      onClick={(event) => review.toggle(event.currentTarget)}
    >
      <strong>{count}</strong>
      <span>awaiting review</span>
    </button>
  );
}

function groupByDate(jobs: TodayJob[]): [string, TodayJob[]][] {
  const groups = new Map<string, TodayJob[]>();
  for (const job of jobs) {
    groups.set(job.scheduledDate, [
      ...(groups.get(job.scheduledDate) ?? []),
      job,
    ]);
  }

  return [...groups.entries()];
}

function reviewDateLabel(date: string, currentDate: string): string {
  const formatted = new Intl.DateTimeFormat("en", {
    weekday: "short",
    day: "numeric",
    month: "short",
  }).format(new Date(`${date}T12:00:00`));
  if (date === currentDate) {
    return `Today · ${formatted}`;
  }
  if (date === addDays(currentDate, -1)) {
    return `Yesterday · ${formatted}`;
  }

  return formatted;
}
