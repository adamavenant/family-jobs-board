import { expect, test } from "@playwright/test";
import type { Page, Route } from "@playwright/test";
import type { components } from "../src/api/schema";

type AuthResponse = components["schemas"]["AuthResponse"];
type CalendarResponse = components["schemas"]["CalendarResponse"];
type TodayResponse = components["schemas"]["TodayResponse"];
type JobResponse = components["schemas"]["JobResponse"];
type MemberResponse = components["schemas"]["MemberResponse"];

const currentDate = "2026-09-22";
const addie: MemberResponse = {
  id: "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c",
  firstName: "Addie",
  nickname: null,
  displayName: "Addie",
  isAdult: true,
};
const fredster: MemberResponse = {
  id: "754de05d-b6f6-4626-bbad-79e2079cc5c3",
  firstName: "Fredster",
  nickname: null,
  displayName: "Fredster",
  isAdult: false,
};

const viewports = [
  { name: "phone", width: 375, height: 812 },
  { name: "tablet", width: 768, height: 1024 },
  { name: "desktop", width: 1280, height: 800 },
];

for (const viewport of viewports) {
  test(`a grown-up reviews earlier days' work from the calendar on a ${viewport.name}`, async ({
    page,
  }) => {
    await page.setViewportSize({
      width: viewport.width,
      height: viewport.height,
    });
    const jobs = [
      job("old", "Feed the dog", "2026-09-20"),
      job("new", "Pack school bag", currentDate),
    ];
    await page.route("**/api/**", async (route) => {
      const url = new URL(route.request().url());
      if (!url.pathname.startsWith("/api/")) {
        return route.continue();
      }
      if (url.pathname === "/api/auth/refresh") {
        return json(route, auth());
      }
      const decision = url.pathname.match(/^\/api\/jobs\/([^/]+)\/approve$/);
      if (decision) {
        const target = jobs.find((item) => item.id === decision[1])!;
        target.status = "approved";
        return json(route, { job: target, pointsBalance: 5 });
      }
      if (url.pathname === "/api/approvals/pending") {
        return json(route, {
          jobs: jobs.filter((item) => item.status === "pendingApproval"),
        });
      }
      if (url.pathname === "/api/calendar") {
        return json(route, week(jobs));
      }
      if (url.pathname === "/api/today") {
        return json(route, today(jobs));
      }
      return route.fulfill({ status: 404 });
    });

    // A fresh page load restores the session on the board, so start there.
    await page.goto("/");
    await page.getByRole("link", { name: "Calendar" }).click();
    await expect(page.getByRole("heading", { name: "Calendar" })).toBeVisible();
    const badge = page.getByRole("button", { name: "2 jobs awaiting review" });
    await badge.click();
    const panel = page.getByRole("complementary", { name: "Awaiting review" });
    await expect(
      panel.getByText("Feed the dog", { exact: true }),
    ).toBeVisible();
    await expect(panel).toBeInViewport();
    await expectNoSidewaysScroll(page);

    await panel
      .getByRole("button", { name: "Approve +5 Feed the dog for Fredster" })
      .click();
    await expect(panel.getByText("Feed the dog", { exact: true })).toHaveCount(
      0,
    );
    await expect(
      page.getByRole("button", { name: "1 jobs awaiting review" }),
    ).toBeVisible();

    await page.keyboard.press("Escape");
    await expect(panel).toHaveCount(0);
    await expect(
      page.getByRole("button", { name: "1 jobs awaiting review" }),
    ).toBeFocused();

    const row = page.locator(".calendar-job-row--pending");
    await row.scrollIntoViewIfNeeded();
    await expect(
      row.getByRole("button", {
        name: "Approve +5 Pack school bag for Fredster",
      }),
    ).toBeVisible();
    await expectNoSidewaysScroll(page);
  });
}

async function expectNoSidewaysScroll(page: Page) {
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
}

function job(id: string, name: string, scheduledDate: string): JobResponse {
  return {
    id,
    childId: fredster.id,
    childDisplayName: fredster.displayName,
    name,
    description: "",
    points: 5,
    scheduledDate,
    agendaPeriod: "morning",
    scheduledTime: null,
    recurringJobSeriesId: null,
    recurrenceFrequency: null,
    status: "pendingApproval",
    completedAtUtc: "2026-09-20T15:00:00Z",
    approvedAtUtc: null,
    latestRejection: null,
    recurrence: null,
  };
}

function week(jobs: JobResponse[]): CalendarResponse {
  const start = "2026-09-21";
  const days = Array.from({ length: 7 }, (_, index) => {
    const value = new Date(`${start}T12:00:00Z`);
    value.setUTCDate(value.getUTCDate() + index);
    const date = value.toISOString().slice(0, 10);
    return {
      date,
      isInFocusedPeriod: true,
      jobs: jobs.filter((item) => item.scheduledDate === date),
    };
  });
  return {
    viewer: addie,
    members: [addie, fredster],
    view: "week",
    anchorDate: currentDate,
    currentDate,
    rangeStart: start,
    rangeEnd: "2026-09-27",
    selectedChildId: null,
    pendingApprovalCount: jobs.filter(
      (item) => item.status === "pendingApproval",
    ).length,
    days,
  };
}

function today(jobs: JobResponse[]): TodayResponse {
  return {
    viewer: addie,
    members: [addie, fredster],
    date: currentDate,
    currentDate,
    selectedChildId: null,
    jobs: jobs.filter((item) => item.scheduledDate === currentDate),
    pointsBalance: null,
    pendingApprovalCount: jobs.filter(
      (item) => item.status === "pendingApproval",
    ).length,
    whoseTurns: [],
  };
}

function auth(): AuthResponse {
  return {
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id: addie.id, displayName: addie.displayName, role: "adult" },
  };
}

async function json(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}
