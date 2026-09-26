import type { LoaderFunctionArgs } from "react-router";
import { redirect } from "react-router";

import { CalendarApiError, getCalendar } from "../../api/calendar";
import type { CalendarBoard, CalendarView } from "../../api/calendar";
import { currentSession } from "../../api/auth";

export type CalendarLoaderData =
  { state: "ready"; board: CalendarBoard } | { state: "error"; error: string };

export async function calendarLoader({
  request,
}: LoaderFunctionArgs): Promise<CalendarLoaderData> {
  const session = currentSession();
  if (!session || session.member.role !== "adult") {
    // Children never see a link to this page; a direct visit sends them back to their board.
    throw redirect("/");
  }

  const searchParams = new URL(request.url).searchParams;
  const view = parseView(searchParams.get("view"));
  const date = searchParams.get("date") ?? undefined;
  const childId = searchParams.get("childId") ?? undefined;

  try {
    return { state: "ready", board: await getCalendar(view, date, childId) };
  } catch (error) {
    return {
      state: "error",
      error:
        error instanceof CalendarApiError
          ? error.message
          : "We couldn't load the calendar.",
    };
  }
}

function parseView(value: string | null): CalendarView | undefined {
  return value === "day" || value === "week" || value === "month"
    ? value
    : undefined;
}
