import { expect, test } from "@playwright/test";
import type { Page, Route } from "@playwright/test";

const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";
const fredsterId = "754de05d-b6f6-4626-bbad-79e2079cc5c3";
const date = "2026-08-29";

interface Sent {
  method: string;
  path: string;
}

/**
 * A household with three jobs waiting for review, a 10-second undo time,
 * and an API that records every save. `refuse` makes approvals fail with 409.
 */
async function household(page: Page, { refuse = false } = {}) {
  const state = {
    sent: [] as Sent[],
    approved: new Set<string>(),
  };
  const jobs = ["Clear the table", "Feed the dog", "Water the plants"].map(
    (name, index) => ({
      id: `00000000-0000-4000-8000-00000000000${index + 1}`,
      childId: fredsterId,
      childDisplayName: "Fredster",
      name,
      description: "",
      points: 5,
      scheduledDate: date,
      agendaPeriod: "evening",
      scheduledTime: null,
      recurringJobSeriesId: null,
      recurrenceFrequency: null,
      status: "pendingApproval",
      completedAtUtc: `${date}T09:00:00Z`,
      approvedAtUtc: null,
      latestRejection: null,
    }),
  );

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (!path.startsWith("/api/")) {
      return route.continue();
    }
    // Record saves; session refreshes on page load aren't actions.
    if (request.method() !== "GET" && path !== "/api/auth/refresh") {
      state.sent.push({ method: request.method(), path });
    }
    if (path === "/api/auth/refresh") {
      return state.sent.some((sent) => sent.path === "/api/auth/logout")
        ? problem(route, 401, "Signed out.")
        : json(route, {
            accessToken: "test-access-token",
            accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
            member: { id: addieId, displayName: "Addie", role: "adult" },
          });
    }
    if (path === "/api/auth/logout") {
      return route.fulfill({ status: 204 });
    }
    if (path === "/api/auth/start") {
      return json(route, {
        state: "signIn",
        members: [
          {
            id: addieId,
            displayName: "Addie",
            role: "adult",
            requiresSurname: false,
          },
        ],
      });
    }
    if (path.endsWith("/approve")) {
      const id = path.split("/")[3]!;
      if (refuse) {
        return problem(
          route,
          409,
          "This job's points changed. Check the job and try again.",
        );
      }
      state.approved.add(id);
      return json(route, { job: jobs[0], pointsBalance: 5 });
    }
    if (path === "/api/today") {
      const shown = jobs.map((job) =>
        state.approved.has(job.id)
          ? { ...job, status: "approved", approvedAtUtc: `${date}T10:00:00Z` }
          : job,
      );
      return json(route, {
        viewer: {
          id: addieId,
          firstName: "Addie",
          nickname: null,
          displayName: "Addie",
          isAdult: true,
        },
        members: [
          {
            id: addieId,
            firstName: "Addie",
            nickname: null,
            displayName: "Addie",
            isAdult: true,
          },
          {
            id: fredsterId,
            firstName: "Fredster",
            nickname: null,
            displayName: "Fredster",
            isAdult: false,
          },
        ],
        date,
        currentDate: date,
        selectedChildId: null,
        pointsBalance: null,
        pendingApprovalCount: shown.filter(
          (job) => job.status === "pendingApproval",
        ).length,
        jobs: shown,
        whoseTurns: [],
        undoGracePeriodSeconds: 10,
      });
    }
    return problem(route, 404, "Not found.");
  });

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Good day, Addie!" }),
  ).toBeVisible();
  // Control the undo countdown from here on: time moves only when a test says.
  await page.clock.install();
  await page.clock.pauseAt(new Date(Date.now() + 1_000));
  return state;
}

function card(page: Page, name: string) {
  return page.locator("article.job-card").filter({
    has: page.getByRole("heading", { name }),
  });
}

async function approve(page: Page, name: string) {
  const button = card(page, name).getByRole("button", {
    name: "Approve +5 points",
  });
  // Scroll the job clear of any toasts already stacked at the bottom.
  await button.evaluate((element) =>
    element.scrollIntoView({ block: "center" }),
  );
  await button.click();
  await expect(
    card(page, name).getByText("Approved — 5 points awarded"),
  ).toBeVisible();
}

test("an approval waits out the undo time before it is saved", async ({
  page,
}) => {
  const api = await household(page);

  await approve(page, "Clear the table");
  const toast = page.locator(".undo-toast");
  await expect(toast).toContainText(
    "Approved “Clear the table” for Fredster: +5 points",
  );
  // Announced through the page's persistent status region.
  await expect(page.locator("p.visually-hidden[role=status]")).toHaveText(
    "Approved “Clear the table” for Fredster: +5 points. Undo is available for 10 seconds.",
  );
  await expect(toast.getByText("10s")).toBeVisible();
  await expect(
    page
      .locator(".hero")
      .getByRole("button", { name: "2 jobs awaiting review" }),
  ).toBeVisible();

  await page.clock.runFor(9_000);
  expect(api.sent).toEqual([]);
  await expect(toast.getByText("1s")).toBeVisible();

  await page.clock.runFor(1_000);
  await expect.poll(() => api.sent.length).toBe(1);
  await expect(toast).toHaveCount(0);
  await expect(
    card(page, "Clear the table").getByText("Approved — 5 points awarded"),
  ).toBeVisible();
  await page.clock.runFor(60_000);
  expect(api.sent).toEqual([
    {
      method: "POST",
      path: "/api/jobs/00000000-0000-4000-8000-000000000001/approve",
    },
  ]);
});

test("Undo puts the job back without sending anything", async ({ page }) => {
  const api = await household(page);

  await approve(page, "Clear the table");
  await page.getByRole("button", { name: "Undo" }).click();

  await expect(
    card(page, "Clear the table").getByRole("button", {
      name: "Approve +5 points",
    }),
  ).toBeEnabled();
  await expect(
    page
      .locator(".hero")
      .getByRole("button", { name: "3 jobs awaiting review" }),
  ).toBeVisible();
  await page.clock.runFor(60_000);
  expect(api.sent).toEqual([]);
});

test("dismissing the toast saves straight away", async ({ page }) => {
  const api = await household(page);

  await approve(page, "Clear the table");
  await page.getByRole("button", { name: "Dismiss and save now" }).click();

  await expect.poll(() => api.sent.length).toBe(1);
  await expect(page.locator(".undo-toast")).toHaveCount(0);
});

test("a refused save is explained and the job shows the server's state", async ({
  page,
}) => {
  const api = await household(page, { refuse: true });

  await approve(page, "Clear the table");
  await page.clock.runFor(10_000);

  await expect(page.getByRole("alert")).toHaveText(
    "Not saved: Approved “Clear the table” for Fredster: +5 points. This job's points changed. Check the job and try again.",
  );
  await expect(
    card(page, "Clear the table").getByRole("button", {
      name: "Approve +5 points",
    }),
  ).toBeEnabled();
  await expect(
    page
      .locator(".hero")
      .getByRole("button", { name: "3 jobs awaiting review" }),
  ).toBeVisible();
  expect(api.sent).toHaveLength(1);
});

test("queued actions are sent as soon as the tab is hidden", async ({
  page,
}) => {
  const api = await household(page);

  await approve(page, "Clear the table");
  await approve(page, "Feed the dog");
  expect(api.sent).toEqual([]);
  await page.evaluate(() => {
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      get: () => "hidden",
    });
    document.dispatchEvent(new Event("visibilitychange"));
  });

  await expect.poll(() => api.sent.length).toBe(2);
});

test("queued actions are sent when the page is closed", async ({ page }) => {
  const api = await household(page);

  await approve(page, "Water the plants");
  await page.evaluate(() =>
    window.dispatchEvent(new PageTransitionEvent("pagehide")),
  );

  await expect.poll(() => api.sent.length).toBe(1);
});

test("queued actions are sent before signing out", async ({ page }) => {
  const api = await household(page);

  await approve(page, "Clear the table");
  await page.locator(".identity-menu summary").click();
  await page.getByRole("button", { name: "Sign out" }).click();

  await expect(
    page.getByRole("button", { name: "Open my board" }),
  ).toBeVisible();
  expect(api.sent.map((sent) => sent.path).slice(0, 2)).toEqual([
    "/api/jobs/00000000-0000-4000-8000-000000000001/approve",
    "/api/auth/logout",
  ]);
});

test("keyboard users reach Undo and the countdown pauses while it has keyboard focus", async ({
  page,
}) => {
  const api = await household(page);

  await approve(page, "Clear the table");
  const undo = page.getByRole("button", { name: "Undo" });
  // Arrive on Undo by keyboard, as a keyboard user would.
  await page.getByRole("button", { name: "Dismiss and save now" }).focus();
  await page.keyboard.press("Shift+Tab");
  await expect(undo).toBeFocused();
  await expect(page.locator(".undo-toast").getByText("Paused")).toBeVisible();
  await page.clock.runFor(60_000);
  expect(api.sent).toEqual([]);

  await page.keyboard.press("Enter");
  await expect(undo).toHaveCount(0);
  await expect(
    card(page, "Clear the table").getByRole("button", {
      name: "Approve +5 points",
    }),
  ).toBeVisible();
});

test("several queued actions stack inside a phone screen", async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 640 });
  const api = await household(page);

  await approve(page, "Clear the table");
  await approve(page, "Feed the dog");
  await approve(page, "Water the plants");

  const toasts = page.locator(".undo-toast");
  await expect(toasts).toHaveCount(3);
  for (const box of await Promise.all(
    (await toasts.all()).map((toast) => toast.boundingBox()),
  )) {
    expect(box?.x ?? -1).toBeGreaterThanOrEqual(0);
    expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(360);
  }
  const stack = await page.locator(".toast-stack").evaluate((element) => ({
    height: element.getBoundingClientRect().height,
    scrolls: element.scrollHeight > element.clientHeight,
  }));
  expect(stack.height).toBeLessThanOrEqual(640 * 0.6 + 1);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBe(true);

  await toasts
    .filter({ hasText: "Feed the dog" })
    .getByRole("button", { name: "Undo" })
    .click();
  // A toast under the pointer stays paused, so move away before time passes.
  await page.mouse.move(0, 0);
  await page.clock.runFor(10_000);
  await expect
    .poll(() => api.sent.map((sent) => sent.path).sort())
    .toEqual([
      "/api/jobs/00000000-0000-4000-8000-000000000001/approve",
      "/api/jobs/00000000-0000-4000-8000-000000000003/approve",
    ]);
});

async function json(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body),
  });
}

async function problem(route: Route, status: number, detail: string) {
  await route.fulfill({
    status,
    contentType: "application/problem+json",
    body: JSON.stringify({ detail }),
  });
}
