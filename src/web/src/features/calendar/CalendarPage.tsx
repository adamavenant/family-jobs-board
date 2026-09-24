import { Link, useLoaderData } from "react-router";

import type { TodayJob } from "../../api/today";
import type {
  CalendarBoard,
  CalendarDay,
  CalendarView,
} from "../../api/calendar";
import { ThemeToggle } from "../theme/ThemeToggle";
import { IdentityControls } from "../today/TodayPage";
import {
  addDays,
  addMonths,
  boardHref,
  formatAgendaPeriod,
  formatRecurrenceFrequency,
  jobStatusClassName,
  jobStatusLabel,
} from "../today/jobFormatting";
import type { CalendarLoaderData } from "./calendarRoute";

const viewLabels: Record<CalendarView, string> = {
  day: "Day",
  week: "Week",
  month: "Month",
};

export function CalendarPage() {
  const data = useLoaderData() as CalendarLoaderData;

  if (data.state === "error") {
    return (
      <main>
        <p className="error-message" role="alert">
          {data.error}
        </p>
        <p>
          <Link to="/">Back to today</Link>
        </p>
      </main>
    );
  }

  const { board } = data;
  const children = board.members.filter((member) => !member.isAdult);

  return (
    <main>
      <header className="hero">
        <div className="hero__toolbar">
          <p className="eyebrow">Family Jobs Board</p>
          <div className="hero__actions">
            <IdentityControls viewer={board.viewer} />
            <ThemeToggle />
          </div>
        </div>
        <div className="hero__content">
          <div>
            <h1>Calendar</h1>
            <p className="hero__date">
              <Link to="/">← Back to today</Link>
            </p>
          </div>
        </div>
      </header>

      <section className="board" aria-labelledby="calendar-heading">
        <nav className="child-filter" aria-label="Filter calendar by child">
          <span>Show jobs for</span>
          <div>
            <Link
              to={calendarHref(board.view, board.anchorDate, null)}
              aria-current={board.selectedChildId === null ? "page" : undefined}
            >
              All children
            </Link>
            {children.map((child) => (
              <Link
                key={child.id}
                to={calendarHref(board.view, board.anchorDate, child.id)}
                aria-current={
                  board.selectedChildId === child.id ? "page" : undefined
                }
              >
                {child.displayName}
              </Link>
            ))}
          </div>
        </nav>

        <nav className="view-switcher" aria-label="Choose a calendar view">
          {(Object.keys(viewLabels) as CalendarView[]).map((view) => (
            <Link
              key={view}
              to={calendarHref(view, board.anchorDate, board.selectedChildId)}
              aria-current={board.view === view ? "page" : undefined}
            >
              {viewLabels[view]}
            </Link>
          ))}
        </nav>

        <nav className="date-navigation" aria-label="Calendar navigation">
          <Link
            to={calendarHref(
              board.view,
              stepAnchor(board.view, board.anchorDate, -1),
              board.selectedChildId,
            )}
          >
            ← Previous {viewLabels[board.view].toLowerCase()}
          </Link>
          <Link
            to={calendarHref(
              board.view,
              board.currentDate,
              board.selectedChildId,
            )}
          >
            Today
          </Link>
          <Link
            to={calendarHref(
              board.view,
              stepAnchor(board.view, board.anchorDate, 1),
              board.selectedChildId,
            )}
          >
            Next {viewLabels[board.view].toLowerCase()} →
          </Link>
        </nav>

        <div className="board__heading">
          <div>
            <p className="eyebrow">{viewLabels[board.view]} view</p>
            <h2 id="calendar-heading">{rangeHeading(board)}</h2>
          </div>
          <p>
            Browsing jobs. Open a job’s date to complete, approve, or reject it
            from today’s board.
          </p>
        </div>

        {board.view === "day" ? (
          <DayView day={board.days[0]!} currentDate={board.currentDate} />
        ) : board.view === "week" ? (
          <WeekView board={board} />
        ) : (
          <MonthView board={board} />
        )}
      </section>
    </main>
  );
}

function DayView({
  day,
  currentDate,
}: {
  day: CalendarDay;
  currentDate: string;
}) {
  return (
    <ol className="calendar-day-list" aria-label={day.date}>
      {day.jobs.length === 0 ? (
        <p className="board__empty">No jobs are scheduled for this day.</p>
      ) : (
        day.jobs.map((job) => (
          <CalendarJobRow
            key={job.id}
            job={job}
            currentDate={currentDate}
            detailed
          />
        ))
      )}
    </ol>
  );
}

function WeekView({ board }: { board: CalendarBoard }) {
  return (
    <div className="calendar-week">
      {board.days.map((day) => (
        <section
          key={day.date}
          className={
            day.date === board.currentDate
              ? "calendar-week__day calendar-week__day--today"
              : "calendar-week__day"
          }
          aria-label={day.date}
        >
          <h3>{weekdayHeading(day.date, board.currentDate)}</h3>
          {day.jobs.length === 0 ? (
            <p className="calendar-week__empty">No jobs</p>
          ) : (
            <ol>
              {day.jobs.map((job) => (
                <CalendarJobRow
                  key={job.id}
                  job={job}
                  currentDate={board.currentDate}
                />
              ))}
            </ol>
          )}
        </section>
      ))}
    </div>
  );
}

function MonthView({ board }: { board: CalendarBoard }) {
  return (
    <ul className="calendar-month" aria-label="Month">
      {board.days.map((day) => (
        <li key={day.date}>
          <Link
            to={calendarHref("day", day.date, board.selectedChildId)}
            className={
              day.date === board.currentDate
                ? "calendar-month__day calendar-month__day--today"
                : day.isInFocusedPeriod
                  ? "calendar-month__day"
                  : "calendar-month__day calendar-month__day--outside"
            }
          >
            <span className="calendar-month__date">{dayNumber(day.date)}</span>
            {day.jobs.slice(0, 3).map((job) => (
              <MonthChip
                key={job.id}
                job={job}
                currentDate={board.currentDate}
              />
            ))}
            {day.jobs.length > 3 ? (
              <span className="calendar-month__more">
                +{day.jobs.length - 3} more
              </span>
            ) : null}
          </Link>
        </li>
      ))}
    </ul>
  );
}

function MonthChip({
  job,
  currentDate,
}: {
  job: TodayJob;
  currentDate: string;
}) {
  const isOverdue = job.status === "open" && job.scheduledDate < currentDate;

  return (
    <span className={`calendar-month__chip ${jobStatusClassName(job.status)}`}>
      <span className="calendar-month__chip-name">{job.name}</span>
      <span className="calendar-month__chip-status">
        {jobStatusLabel(job.status)}
        {isOverdue ? " · Overdue" : ""}
      </span>
    </span>
  );
}

function CalendarJobRow({
  job,
  currentDate,
  detailed = false,
}: {
  job: TodayJob;
  currentDate: string;
  detailed?: boolean;
}) {
  const isOverdue = job.status === "open" && job.scheduledDate < currentDate;

  return (
    <li className="calendar-job-row">
      <Link to={boardHref(job.scheduledDate, currentDate, job.childId)}>
        <span className={jobStatusClassName(job.status)}>
          {jobStatusLabel(job.status)}
        </span>
        <span className="calendar-job-row__name">
          {job.name}
          {isOverdue ? (
            <em className="calendar-job-row__overdue"> · overdue</em>
          ) : null}
        </span>
        <span className="calendar-job-row__meta">
          For {job.childDisplayName} · {job.points} pts
          {detailed
            ? ` · ${job.recurringJobSeriesId ? `${formatRecurrenceFrequency(job.recurrenceFrequency)} · ` : ""}${formatAgendaPeriod(job.agendaPeriod)}`
            : ""}
        </span>
      </Link>
    </li>
  );
}

function rangeHeading(board: CalendarBoard): string {
  if (board.view === "day") {
    return formatDate(board.anchorDate);
  }

  if (board.view === "week") {
    return `${formatDate(board.rangeStart, { month: "short" })} – ${formatDate(board.rangeEnd, { month: "short" })}`;
  }

  return new Intl.DateTimeFormat("en", {
    month: "long",
    year: "numeric",
  }).format(new Date(`${board.anchorDate}T12:00:00`));
}

function weekdayHeading(date: string, currentDate: string): string {
  const label = new Intl.DateTimeFormat("en", {
    weekday: "short",
    day: "numeric",
  }).format(new Date(`${date}T12:00:00`));
  return date === currentDate ? `${label} · Today` : label;
}

function dayNumber(date: string): string {
  return String(Number(date.slice(8, 10)));
}

function formatDate(
  date: string,
  options: Intl.DateTimeFormatOptions = {},
): string {
  return new Intl.DateTimeFormat("en", {
    day: "numeric",
    month: "long",
    ...options,
  }).format(new Date(`${date}T12:00:00`));
}

function stepAnchor(
  view: CalendarView,
  anchorDate: string,
  direction: 1 | -1,
): string {
  if (view === "day") {
    return addDays(anchorDate, direction);
  }

  if (view === "week") {
    return addDays(anchorDate, direction * 7);
  }

  return addMonths(anchorDate, direction);
}

function calendarHref(
  view: CalendarView,
  date: string,
  selectedChildId: string | null,
): string {
  const search = new URLSearchParams({ view, date });
  if (selectedChildId) {
    search.set("childId", selectedChildId);
  }

  return `/calendar?${search.toString()}`;
}
