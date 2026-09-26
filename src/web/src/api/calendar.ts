import createClient from "openapi-fetch";

import type { paths } from "./schema";
import { authenticatedFetch } from "./auth";
import { mapJob } from "./today";
import type { HouseholdMember, TodayJob } from "./today";

export type CalendarView = "day" | "week" | "month";

export interface CalendarDay {
  date: string;
  isInFocusedPeriod: boolean;
  jobs: TodayJob[];
}

export interface CalendarBoard {
  viewer: HouseholdMember;
  members: HouseholdMember[];
  view: CalendarView;
  anchorDate: string;
  currentDate: string;
  rangeStart: string;
  rangeEnd: string;
  selectedChildId: string | null;
  days: CalendarDay[];
}

export class CalendarApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "CalendarApiError";
  }
}

export async function getCalendar(
  view?: CalendarView,
  date?: string,
  childId?: string,
): Promise<CalendarBoard> {
  const client = createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
  const { data, error } = await client.GET("/api/calendar", {
    params: {
      query: {
        ...(view ? { view } : {}),
        ...(date ? { date } : {}),
        ...(childId ? { childId } : {}),
      },
    },
  });
  if (!data) {
    throw new CalendarApiError(
      problemMessage(error, "We couldn't load the calendar."),
    );
  }

  return {
    viewer: data.viewer,
    members: data.members,
    view: isCalendarView(data.view) ? data.view : "week",
    anchorDate: data.anchorDate,
    currentDate: data.currentDate,
    rangeStart: data.rangeStart,
    rangeEnd: data.rangeEnd,
    selectedChildId: data.selectedChildId,
    days: data.days.map((day) => ({
      date: day.date,
      isInFocusedPeriod: day.isInFocusedPeriod,
      jobs: day.jobs.map(mapJob),
    })),
  };
}

function isCalendarView(value: string): value is CalendarView {
  return value === "day" || value === "week" || value === "month";
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object") {
    const errors = "errors" in error ? error.errors : undefined;
    if (errors && typeof errors === "object") {
      for (const messages of Object.values(errors)) {
        if (Array.isArray(messages) && typeof messages[0] === "string") {
          return messages[0];
        }
      }
    }

    const detail = "detail" in error ? error.detail : undefined;
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }
  }

  return fallback;
}
