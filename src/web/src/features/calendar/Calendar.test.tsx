import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { acceptSession, clearIdentity } from "../../api/auth";
import { routes } from "../../app/routes";

const addie = {
  id: "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c",
  displayName: "Addie",
  isAdult: true,
};
const fredster = {
  id: "754de05d-b6f6-4626-bbad-79e2079cc5c3",
  displayName: "Fredster",
  isAdult: false,
};
const harrie = {
  id: "e22facf5-69ce-45ce-9dad-306eef1852c9",
  displayName: "Harrie",
  isAdult: false,
};
const currentDate = "2026-09-22";

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
  window.localStorage.clear();
});

describe("Calendar", () => {
  it("shows the week containing today by default and can switch to day and month views", async () => {
    stubFetch({
      week: weekBoard([rawJob({ id: "job-week", name: "Feed the dog" })]),
      day: dayBoard([rawJob({ id: "job-day", name: "Pack school bag" })]),
      month: monthBoard(),
    });
    const user = userEvent.setup();
    renderCalendar();

    await screen.findByRole("heading", { name: "Calendar" });
    expect(
      screen.getByRole("link", { name: "Week", current: "page" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Feed the dog")).toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: "Day" }));
    await screen.findByRole("link", { name: "Day", current: "page" });
    expect(await screen.findByText("Pack school bag")).toBeInTheDocument();

    await user.click(screen.getByRole("link", { name: "Month" }));
    await screen.findByRole("link", { name: "Month", current: "page" });
    // Plain semantic list markup, not a fake ARIA grid the page doesn't implement keyboard
    // navigation for.
    expect(screen.getAllByRole("listitem")).toHaveLength(42);
  });

  it("marks days outside the focused month and highlights today", async () => {
    stubFetch({ month: monthBoard() });
    renderCalendar("/calendar?view=month&date=2026-09-22");

    await screen.findByRole("heading", { name: "Calendar" });
    const dayLinks = screen
      .getAllByRole("listitem")
      .map((item) => within(item).getByRole("link"));
    const outside = dayLinks.filter((link) =>
      link.className.includes("calendar-month__day--outside"),
    );
    const today = dayLinks.filter((link) =>
      link.className.includes("calendar-month__day--today"),
    );
    expect(outside.length).toBeGreaterThan(0);
    expect(today).toHaveLength(1);
  });

  it("shows each month-view job's status and overdue state as readable text, not just color", async () => {
    stubFetch({
      month: monthBoard([
        rawJob({
          id: "job-overdue",
          name: "Overdue chore",
          scheduledDate: "2026-09-10",
          childId: harrie.id,
          childDisplayName: harrie.displayName,
        }),
      ]),
    });
    renderCalendar("/calendar?view=month&date=2026-09-10");

    await screen.findByText("Overdue chore");
    expect(screen.getByText("Ready to do · Overdue")).toBeInTheDocument();
  });

  it("shows a past incomplete job marked as overdue and links back to its date", async () => {
    stubFetch({
      day: dayBoard(
        [
          rawJob({
            id: "job-overdue",
            name: "Overdue chore",
            scheduledDate: "2026-09-10",
            childId: harrie.id,
            childDisplayName: harrie.displayName,
          }),
        ],
        "2026-09-10",
      ),
    });
    renderCalendar("/calendar?view=day&date=2026-09-10");

    await screen.findByText("Overdue chore");
    expect(screen.getByText("· overdue")).toBeInTheDocument();
    const link = screen.getByRole("link", { name: /Overdue chore/ });
    expect(link).toHaveAttribute(
      "href",
      expect.stringContaining("date=2026-09-10"),
    );
    expect(link).toHaveAttribute(
      "href",
      expect.stringContaining(`childId=${harrie.id}`),
    );
  });

  it("filters the calendar by child", async () => {
    const fetchMock = stubFetch({
      week: weekBoard([
        rawJob({ id: "job-1", name: "Fredster's job", childId: fredster.id }),
      ]),
    });
    const filtered = weekBoard(
      [rawJob({ id: "job-2", name: "Harrie's job", childId: harrie.id })],
      harrie.id,
    );
    fetchMock.mockImplementation(async (input: RequestInfo | URL) => {
      const url = new URL(
        (input as Request).url ?? (input as string),
        "http://localhost",
      );
      if (
        url.pathname === "/api/calendar" &&
        url.searchParams.get("childId") === harrie.id
      ) {
        return jsonResponse(filtered);
      }
      return jsonResponse(
        weekBoard([rawJob({ id: "job-1", name: "Fredster's job" })]),
      );
    });
    const user = userEvent.setup();
    renderCalendar();

    await screen.findByText("Fredster's job");
    await user.click(screen.getByRole("link", { name: "Harrie" }));

    expect(await screen.findByText("Harrie's job")).toBeInTheDocument();
    expect(screen.queryByText("Fredster's job")).not.toBeInTheDocument();
  });

  it("sends a signed-in child straight back to their own board", async () => {
    stubFetch({ today: true });
    renderCalendar("/calendar", fredster);

    expect(
      await screen.findByRole("heading", { name: "Good day, Fredster!" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "Calendar" }),
    ).not.toBeInTheDocument();
  });

  it("offers the calendar link to an adult on the daily board", async () => {
    stubFetch({ today: true, todayViewer: addie });
    renderCalendar("/", addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    expect(screen.getByRole("link", { name: "Calendar" })).toBeInTheDocument();
  });

  it("does not offer the calendar link to a child on their own board", async () => {
    stubFetch({ today: true });
    renderCalendar("/", fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    expect(
      screen.queryByRole("link", { name: "Calendar" }),
    ).not.toBeInTheDocument();
  });
});

function stubFetch(options: {
  week?: unknown;
  day?: unknown;
  month?: unknown;
  today?: boolean;
  todayViewer?: typeof addie | typeof fredster;
}) {
  const todayViewer = options.todayViewer ?? fredster;
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const request = input as Request;
    const url = new URL(request.url ?? (input as string), "http://localhost");
    if (url.pathname === "/api/calendar") {
      const view = url.searchParams.get("view") ?? "week";
      if (view === "day" && options.day) {
        return jsonResponse(options.day);
      }
      if (view === "month" && options.month) {
        return jsonResponse(options.month);
      }
      return jsonResponse(options.week ?? weekBoard());
    }

    return jsonResponse({
      viewer: todayViewer,
      members: [addie, fredster, harrie],
      date: currentDate,
      currentDate,
      selectedChildId: null,
      jobs: [],
      pointsBalance: todayViewer.isAdult ? null : 0,
      pointEarnings: [],
      pendingApprovalCount: 0,
    });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function rawJob(overrides: {
  id: string;
  name: string;
  scheduledDate?: string;
  childId?: string;
  childDisplayName?: string;
}) {
  return {
    id: overrides.id,
    childId: overrides.childId ?? fredster.id,
    childDisplayName: overrides.childDisplayName ?? fredster.displayName,
    name: overrides.name,
    description: "",
    points: 5,
    scheduledDate: overrides.scheduledDate ?? currentDate,
    agendaPeriod: "morning",
    scheduledTime: null,
    recurringJobSeriesId: null,
    recurrenceFrequency: null,
    status: "open",
    completedAtUtc: null,
    approvedAtUtc: null,
    latestRejection: null,
  };
}

function dayBoard(
  jobs: unknown[],
  date = currentDate,
  selectedChildId: string | null = null,
) {
  return {
    viewer: addie,
    members: [addie, fredster, harrie],
    view: "day",
    anchorDate: date,
    currentDate,
    rangeStart: date,
    rangeEnd: date,
    selectedChildId,
    days: [{ date, isInFocusedPeriod: true, jobs }],
  };
}

function weekBoard(
  jobs: unknown[] = [],
  selectedChildId: string | null = null,
) {
  const start = "2026-09-21";
  const days = Array.from({ length: 7 }, (_, index) => {
    const date = addDays(start, index);
    return {
      date,
      isInFocusedPeriod: true,
      jobs: date === currentDate ? jobs : [],
    };
  });
  return {
    viewer: addie,
    members: [addie, fredster, harrie],
    view: "week",
    anchorDate: currentDate,
    currentDate,
    rangeStart: start,
    rangeEnd: addDays(start, 6),
    selectedChildId,
    days,
  };
}

function monthBoard(jobs: ReturnType<typeof rawJob>[] = []) {
  const start = "2026-08-31"; // Monday grid start for September 2026
  const days = Array.from({ length: 42 }, (_, index) => {
    const date = addDays(start, index);
    const [, month] = date.split("-");
    return {
      date,
      isInFocusedPeriod: month === "09",
      jobs: jobs.filter((job) => job.scheduledDate === date),
    };
  });
  return {
    viewer: addie,
    members: [addie, fredster, harrie],
    view: "month",
    anchorDate: currentDate,
    currentDate,
    rangeStart: start,
    rangeEnd: addDays(start, 41),
    selectedChildId: null,
    days,
  };
}

function addDays(date: string, days: number): string {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

function renderCalendar(
  path = "/calendar",
  viewer: typeof addie | typeof fredster = addie,
) {
  acceptSession({
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: {
      id: viewer.id,
      displayName: viewer.displayName,
      role: viewer.isAdult ? "adult" : "child",
    },
  });
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  return render(<RouterProvider router={router} />);
}

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
}
