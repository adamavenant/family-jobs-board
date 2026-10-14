import { useEffect, useRef, useState } from "react";
import type { RefObject } from "react";
import { Link } from "react-router";

import { ApprovalsTrigger } from "../approvals/ApprovalsPanel";
import { KittenIllustration } from "./KittenIllustration";

/**
 * Keeps the viewed date in sight once the page header's date line scrolls away, with the same
 * day navigation and, for grown-ups, the review badge.
 */
export function FloatingDateBar({
  dateLineRef,
  date,
  isToday,
  previousHref,
  nextHref,
  pendingApprovalCount,
}: {
  dateLineRef: RefObject<HTMLElement | null>;
  date: string;
  isToday: boolean;
  previousHref: string;
  nextHref: string;
  pendingApprovalCount: number | null;
}) {
  const [isPinned, setIsPinned] = useState(false);
  const barRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const dateLine = dateLineRef.current;
    if (!dateLine || typeof IntersectionObserver === "undefined") {
      return;
    }

    const observer = new IntersectionObserver(([entry]) => {
      // Pin only once the date line has left through the top, not while it's still below.
      setIsPinned(
        entry !== undefined &&
          !entry.isIntersecting &&
          entry.boundingClientRect.top < 0,
      );
    });
    observer.observe(dateLine);
    return () => observer.disconnect();
  }, [dateLineRef]);

  useEffect(() => {
    if (!isPinned) {
      return;
    }

    // Browsers don't reliably apply scroll-padding when focus lands on a control that is
    // already in the viewport, so lift any newly focused page control out from under the bar.
    function keepFocusClear(event: FocusEvent) {
      const bar = barRef.current;
      const target = event.target;
      if (!bar || !(target instanceof Element) || isInFixedLayer(target)) {
        return;
      }

      const overlap =
        bar.getBoundingClientRect().bottom - target.getBoundingClientRect().top;
      if (overlap > 0) {
        window.scrollBy({ top: -(overlap + 12) });
      }
    }

    document.addEventListener("focusin", keepFocusClear);
    return () => document.removeEventListener("focusin", keepFocusClear);
  }, [isPinned]);

  if (!isPinned) {
    return null;
  }

  const shortDate = new Intl.DateTimeFormat("en", {
    weekday: "short",
    day: "numeric",
    month: "short",
  }).format(new Date(`${date}T12:00:00`));

  return (
    <div className="floating-date-bar" ref={barRef}>
      <nav className="floating-date-bar__inner" aria-label="Pinned day">
        <KittenIllustration className="floating-date-bar__kitten" />
        <p className="floating-date-bar__date">
          <time dateTime={date}>{shortDate}</time>
          {isToday ? (
            <span className="floating-date-bar__today">Today</span>
          ) : null}
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

/** Controls in fixed layers (this bar, the review panel, toasts) don't move when the page scrolls. */
function isInFixedLayer(element: Element): boolean {
  for (
    let current: Element | null = element;
    current;
    current = current.parentElement
  ) {
    if (getComputedStyle(current).position === "fixed") {
      return true;
    }
  }

  return false;
}
