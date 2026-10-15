import { useEffect, useState } from "react";
import type { RefObject } from "react";
import { Link } from "react-router";

import { ApprovalsTrigger } from "../approvals/ApprovalsPanel";
import { formatShortDate } from "./jobFormatting";
import { KittenIllustration } from "./KittenIllustration";

/** The bar's height in pixels, matching `.floating-date-bar__inner`. */
const barHeight = 64;

/**
 * Keeps the viewed date in sight once the page header's date line scrolls away, with the same
 * day navigation and, for grown-ups, the review badge.
 */
export function FloatingDateBar({
  dateLineRef,
  date,
  isToday,
  todayHref,
  previousHref,
  nextHref,
  pendingApprovalCount,
}: {
  dateLineRef: RefObject<HTMLElement | null>;
  date: string;
  isToday: boolean;
  todayHref: string;
  previousHref: string;
  nextHref: string;
  pendingApprovalCount: number | null;
}) {
  const [isPinned, setIsPinned] = useState(false);

  useEffect(() => {
    const dateLine = dateLineRef.current;
    if (!dateLine || typeof IntersectionObserver === "undefined") {
      return;
    }

    // Pin as the date line slides under where the bar sits, and only when it leaves through
    // the top rather than while it's still below the fold.
    const observer = new IntersectionObserver(
      ([entry]) => {
        setIsPinned(
          entry !== undefined &&
            !entry.isIntersecting &&
            entry.boundingClientRect.top < barHeight,
        );
      },
      { rootMargin: `-${barHeight}px 0px 0px 0px` },
    );
    observer.observe(dateLine);
    return () => observer.disconnect();
  }, [dateLineRef]);

  if (!isPinned) {
    return null;
  }

  return (
    <div className="floating-date-bar">
      <nav className="floating-date-bar__inner" aria-label="Pinned day">
        <KittenIllustration className="floating-date-bar__kitten" />
        <p className="floating-date-bar__date">
          <time dateTime={date}>{formatShortDate(date)}</time>
          {isToday ? (
            <span className="floating-date-bar__today">Today</span>
          ) : (
            <Link to={todayHref} className="floating-date-bar__today">
              Back to today
            </Link>
          )}
        </p>
        <div className="floating-date-bar__actions">
          <Link
            to={previousHref}
            className="floating-date-bar__step"
            aria-label="Previous day"
          >
            <span aria-hidden="true">←</span>
          </Link>
          <Link
            to={nextHref}
            className="floating-date-bar__step"
            aria-label="Next day"
          >
            <span aria-hidden="true">→</span>
          </Link>
          {pendingApprovalCount !== null ? (
            <ApprovalsTrigger
              count={pendingApprovalCount}
              className="floating-date-bar__review"
            />
          ) : null}
        </div>
      </nav>
    </div>
  );
}
