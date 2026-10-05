import { expect, test } from "@playwright/test";
import type { Page, Route } from "@playwright/test";

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

async function expectNoSidewaysScroll(page: Page) {
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
}

function auth(id: string, displayName: string, role: "adult" | "child") {
  return {
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id, displayName, role },
  };
}

function board(isAdult: boolean) {
  const addie = {
    id: addieId,
    firstName: "Addie",
    nickname: null,
    displayName: "Addie",
    isAdult: true,
  };
  const fredster = {
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
