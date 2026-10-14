import { expect, test } from "@playwright/test";
import type { Page, Route } from "@playwright/test";
import type { components } from "../src/api/schema";

type AuthResponse = components["schemas"]["AuthResponse"];
type MemberResponse = components["schemas"]["MemberResponse"];
type TodayResponse = components["schemas"]["TodayResponse"];

const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";
const fredsterId = "754de05d-b6f6-4626-bbad-79e2079cc5c3";

const viewports = [
  { name: "phone", width: 375, height: 812 },
  { name: "tablet", width: 768, height: 1024 },
  { name: "desktop", width: 1280, height: 800 },
];

for (const viewport of viewports) {
  for (const isAdult of [true, false]) {
    const viewer = isAdult ? "grown-up" : "child";

    test(`the ${viewer} board header fits a ${viewport.name} without sideways scrolling`, async ({
      page,
    }) => {
      await page.setViewportSize({
        width: viewport.width,
        height: viewport.height,
      });
      await page.route("**/api/**", async (route) => {
        const path = new URL(route.request().url()).pathname;
        if (!path.startsWith("/api/")) {
          return route.continue();
        }
        if (path === "/api/auth/refresh") {
          return isAdult
            ? json(route, auth(addieId, "Addie", "adult"))
            : json(route, auth(fredsterId, "Fredster", "child"));
        }
        if (path === "/api/today") {
          return json(route, board(isAdult));
        }
        return problem(route, 404);
      });

      await page.goto("/");
      await expect(
        page.getByRole("heading", {
          name: `Good day, ${isAdult ? "Addie" : "Fredster"}!`,
        }),
      ).toBeVisible();
      await expect(page.locator(".theme-toggle")).toBeInViewport({ ratio: 1 });
      await expectNoSidewaysScroll(page);

      await page.locator(".identity-menu summary").click();
      const signOut = page.getByRole("button", { name: "Sign out" });
      await expect(signOut).toBeVisible();
      await expect(page.locator(".identity-menu__panel")).toBeInViewport({
        ratio: 1,
      });
      await expect(signOut).toBeInViewport({ ratio: 1 });
      await expectNoSidewaysScroll(page);
    });
  }
}

for (const viewport of viewports) {
  for (const isAdult of [true, false]) {
    const viewer = isAdult ? "grown-up" : "child";

    test(`the ${viewer} board pins its date on a ${viewport.name} without hiding focused controls`, async ({
      page,
    }) => {
      await page.setViewportSize({
        width: viewport.width,
        height: viewport.height,
      });
      await page.route("**/api/**", async (route) => {
        const path = new URL(route.request().url()).pathname;
        if (!path.startsWith("/api/")) {
          return route.continue();
        }
        if (path === "/api/auth/refresh") {
          return isAdult
            ? json(route, auth(addieId, "Addie", "adult"))
            : json(route, auth(fredsterId, "Fredster", "child"));
        }
        if (path === "/api/today") {
          return json(route, { ...board(isAdult), jobs: manyJobs() });
        }
        return problem(route, 404);
      });

      await page.goto("/");
      const pinned = page.getByRole("navigation", { name: "Pinned day" });
      await expect(pinned).toHaveCount(0);

      await page.mouse.wheel(0, 900);
      await expect(pinned).toBeVisible();
      await expect(pinned.getByText("Sun, Sep 6")).toBeVisible();
      await expect(pinned.getByText("Today")).toBeVisible();
      await expect(
        pinned.getByRole("button", { name: /awaiting review/ }),
      ).toHaveCount(isAdult ? 1 : 0);
      await expect(pinned).toBeInViewport({ ratio: 1 });
      await expectNoSidewaysScroll(page);

      // Find the control Tab reaches next, park it just under the bar, then Tab onto it.
      await page
        .locator(".job-card :is(button, summary):visible")
        .nth(3)
        .focus();
      await page.keyboard.press("Tab");
      const next = await page.evaluateHandle(() => document.activeElement!);
      await page.keyboard.press("Shift+Tab");
      await next.evaluate((element) => {
        const top = element.getBoundingClientRect().top + window.scrollY;
        window.scrollTo(0, top - 10);
      });
      await page.keyboard.press("Tab");
      expect(
        await next.evaluate((element) => element === document.activeElement),
      ).toBe(true);
      const barBottom = await pinned.evaluate(
        (element) => element.getBoundingClientRect().bottom,
      );
      const focusedTop = await next.evaluate(
        (element) => element.getBoundingClientRect().top,
      );
      expect(focusedTop).toBeGreaterThanOrEqual(barBottom);

      await page.evaluate(() => window.scrollTo(0, 0));
      await expect(pinned).toHaveCount(0);
    });
  }
}

function manyJobs(): TodayResponse["jobs"] {
  return Array.from({ length: 8 }, (_, index) => ({
    id: `00000000-0000-4000-8000-00000000000${index}`,
    childId: fredsterId,
    childDisplayName: "Fredster",
    name: `Chore ${index + 1}`,
    description: "A job long enough to give the board some height.",
    points: 2,
    scheduledDate: "2026-09-06",
    agendaPeriod: "morning",
    scheduledTime: null,
    recurringJobSeriesId: null,
    recurrenceFrequency: null,
    status: "open",
    completedAtUtc: null,
    approvedAtUtc: null,
    latestRejection: null,
    recurrence: null,
  }));
}

async function expectNoSidewaysScroll(page: Page) {
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
}

function auth(
  id: string,
  displayName: string,
  role: "adult" | "child",
): AuthResponse {
  return {
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id, displayName, role },
  };
}

function board(isAdult: boolean): TodayResponse {
  const addie: MemberResponse = {
    id: addieId,
    firstName: "Addie",
    nickname: null,
    displayName: "Addie",
    isAdult: true,
  };
  const fredster: MemberResponse = {
    id: fredsterId,
    firstName: "Fredster",
    nickname: null,
    displayName: "Fredster",
    isAdult: false,
  };
  return {
    viewer: isAdult ? addie : fredster,
    members: [addie, fredster],
    date: "2026-09-06",
    currentDate: "2026-09-06",
    selectedChildId: null,
    jobs: [],
    pointsBalance: isAdult ? null : 0,
    pendingApprovalCount: 0,
    whoseTurns: [],
  };
}

async function json(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function problem(route: Route, status: number) {
  await route.fulfill({
    status,
    contentType: "application/problem+json",
    body: JSON.stringify({
      detail: "The session has expired.",
      code: "session_expired",
    }),
  });
}
