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

test("several queued actions fold into one row inside a phone screen", async ({
  page,
}) => {
  await page.setViewportSize({ width: 360, height: 640 });
  const api = await household(page);

  await approve(page, "Clear the table");
  await approve(page, "Feed the dog");
  await approve(page, "Water the plants");

  // Only the newest shows in full; the older two fold into one row.
  const toasts = page.locator(".undo-toast");
  await expect(toasts).toHaveCount(1);
  await expect(toasts).toContainText("Water the plants");
  const row = page.getByRole("button", { name: "2 more waiting Show" });
  await expect(row).toHaveAttribute("aria-expanded", "false");
  await expect(row).toHaveText("2 more waiting · Show");
  // Measure once the web fonts have swapped in, as they change text heights.
  await page.evaluate(() => document.fonts.ready);
  const folded = await stackLayout(page);
  // The stack is just the newest toast and the row.
  expect(folded.items).toHaveLength(2);
  expect(folded.height).toBeLessThanOrEqual(
    folded.items[0]!.height + folded.items[1]!.height + 8 + 1,
  );
  expect(folded.scrolls).toBe(false);
  for (const item of folded.items) {
    expect(item.left).toBeGreaterThanOrEqual(0);
    expect(item.right).toBeLessThanOrEqual(360);
  }
  // The page still keeps room below the stack.
  await expect.poll(() => reservesStackHeight(page)).toBe(true);

  await row.click();
  await expect(toasts).toHaveCount(3);
  await expect(
    page.getByRole("button", { name: "2 more waiting Hide" }),
  ).toHaveAttribute("aria-expanded", "true");
  const shown = await stackLayout(page);
  expect(shown.items).toHaveLength(4);
  for (const item of shown.items) {
    expect(item.left).toBeGreaterThanOrEqual(0);
    expect(item.right).toBeLessThanOrEqual(360);
  }
  expect(shown.height).toBeGreaterThan(folded.height);
  expect(shown.height).toBeLessThanOrEqual(640 * 0.6 + 1);
  await expect.poll(() => reservesStackHeight(page)).toBe(true);
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

test("keyboard users open folded actions, and focus moves on to one when the newest closes", async ({
  page,
}) => {
  const api = await household(page);

  await approve(page, "Clear the table");
  await approve(page, "Feed the dog");
  const newest = page
    .locator(".undo-toast")
    .filter({ hasText: "Feed the dog" });
  const older = page
    .locator(".undo-toast")
    .filter({ hasText: "Clear the table" });
  // Arrive on the folded row by keyboard, as a keyboard user would.
  await newest.getByRole("button", { name: "Dismiss and save now" }).focus();
  await page.keyboard.press("Tab");
  const row = page.getByRole("button", { name: /^1 more waiting/ });
  await expect(row).toBeFocused();
  await expect(row).toHaveAccessibleName("1 more waiting Show");

  await page.keyboard.press("Enter");
  await expect(row).toHaveAccessibleName("1 more waiting Hide");
  await expect(row).toHaveAttribute("aria-expanded", "true");
  // The row holds the action it stood for while it has keyboard focus.
  await expect(older.getByText("Paused")).toBeVisible();
  await page.keyboard.press("Tab");
  await expect(older.getByRole("button", { name: "Undo" })).toBeFocused();

  await page.keyboard.press("Shift+Tab");
  await page.keyboard.press("Shift+Tab");
  await page.keyboard.press("Shift+Tab");
  await expect(newest.getByRole("button", { name: "Undo" })).toBeFocused();
  await page.keyboard.press("Enter");

  await expect(newest).toHaveCount(0);
  await expect(older.getByRole("button", { name: "Undo" })).toBeFocused();
  await expect(older.getByText("Paused")).toBeVisible();
  await page.clock.runFor(60_000);
  expect(api.sent).toEqual([]);
  await expect(
    card(page, "Feed the dog").getByRole("button", {
      name: "Approve +5 points",
    }),
  ).toBeEnabled();
});

/** The stack and everything in it, measured in one go. */
async function stackLayout(page: Page) {
  return page.locator(".toast-stack").evaluate((element) => ({
    height: element.getBoundingClientRect().height,
    scrolls: element.scrollHeight > element.clientHeight,
    items: [...element.children].map((item) => {
      const box = item.getBoundingClientRect();
      return { height: box.height, left: box.left, right: box.right };
    }),
  }));
}

/** The page keeps room below the stack for its current height. */
async function reservesStackHeight(page: Page) {
  return page.locator(".toast-stack").evaluate((element) => {
    const needed = `${Math.ceil(element.getBoundingClientRect().height) + 24}px`;
    return (
      document.documentElement.style.getPropertyValue(
        "--toast-stack-height",
      ) === needed && getComputedStyle(document.body).paddingBottom === needed
    );
  });
}

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
