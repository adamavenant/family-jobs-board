import { expect, test } from "@playwright/test";
import type { Route } from "@playwright/test";

const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";
const fredsterId = "754de05d-b6f6-4626-bbad-79e2079cc5c3";

test("fresh household creates its first grown-up on a phone", async ({
  page,
}) => {
  const pageErrors: string[] = [];
  page.on("pageerror", (error) => pageErrors.push(error.message));
  await page.setViewportSize({ width: 390, height: 844 });
  let bootstrapped = false;
  let submittedPin = "";
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (!path.startsWith("/api/")) {
      return route.continue();
    }
    if (path === "/api/auth/refresh") {
      return problem(route, 401);
    }
    if (path === "/api/auth/start") {
      return json(route, { state: "createFirstAdult" });
    }
    if (path === "/api/auth/bootstrap") {
      submittedPin = String(request.postDataJSON().pin);
      bootstrapped = true;
      return json(route, auth(addieId, "Addie", "adult"), 201);
    }
    if (path === "/api/today" && bootstrapped) {
      return json(route, board("Addie", true, "open"));
    }
    return problem(route, 404);
  });

  await page.goto("/");
  await page.waitForTimeout(250);
  expect(pageErrors).toEqual([]);
  await page.getByLabel("First name").fill("Addie");
  await page.getByLabel("Surname").fill("Avenant");
  await page.getByLabel("6-digit PIN").fill("012345");
  await page.getByLabel("Confirm PIN").fill("012345");
  await page.getByRole("button", { name: "Set up and continue" }).click();

  await expect(
    page.getByRole("heading", { name: "Good day, Addie!" }),
  ).toBeVisible();
  expect(submittedPin).toBe("012345");
  expect(await page.evaluate(() => Object.keys(localStorage))).not.toContain(
    "family-jobs-board-member",
  );
  await expect(page).toHaveURL(/\/$/);
});

test("pilot claim, child handoff, completion, and adult approval work on a tablet", async ({
  page,
}) => {
  await page.setViewportSize({ width: 820, height: 1180 });
  let signedIn: "none" | "adult" | "child" = "none";
  let bootstrapped = false;
  let jobStatus: "open" | "pendingApproval" | "approved" = "open";
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (!path.startsWith("/api/")) {
      return route.continue();
    }
    if (path === "/api/auth/refresh") {
      return problem(route, 401);
    }
    if (path === "/api/auth/start") {
      return json(
        route,
        !bootstrapped
          ? {
              state: "claimExistingAdult",
              adults: [member(addieId, "Addie", "adult")],
            }
          : {
              state: "signIn",
              members: [
                member(addieId, "Addie", "adult"),
                member(fredsterId, "Fredster", "child"),
              ],
            },
      );
    }
    if (path === "/api/auth/bootstrap") {
      bootstrapped = true;
      signedIn = "adult";
      return json(route, auth(addieId, "Addie", "adult"), 201);
    }
    if (path === `/api/users/${fredsterId}/pin-setup`) {
      signedIn = "none";
      return json(route, {
        setupToken: "one-time-token",
        expiresAtUtc: "2099-01-01T00:05:00Z",
        targetDisplayName: "Fredster",
        targetRole: "child",
      });
    }
    if (path === "/api/auth/setup-pin") {
      signedIn = "child";
      return json(route, auth(fredsterId, "Fredster", "child"));
    }
    if (path === "/api/auth/logout") {
      signedIn = "none";
      return route.fulfill({ status: 204 });
    }
    if (path === "/api/auth/sign-in") {
      signedIn = "adult";
      return json(route, auth(addieId, "Addie", "adult"));
    }
    if (path.endsWith("/complete")) {
      jobStatus = "pendingApproval";
      return json(route, job(jobStatus));
    }
    if (path.endsWith("/approve")) {
      jobStatus = "approved";
      return json(route, { job: job(jobStatus), pointsBalance: 3 });
    }
    if (path === "/api/today") {
      return json(
        route,
        board(
          signedIn === "child" ? "Fredster" : "Addie",
          signedIn !== "child",
          jobStatus,
        ),
      );
    }
    return problem(route, 404);
  });

  await page.goto("/");
  await page.getByRole("combobox").selectOption(addieId);
  await page.getByLabel("Surname").fill("Avenant");
  await page.getByLabel("6-digit PIN").fill("012345");
  await page.getByLabel("Confirm PIN").fill("012345");
  await page.getByRole("button", { name: "Set up and continue" }).click();

  await page.locator(".identity-menu summary").click();
  await page.getByLabel("Hand over to").selectOption(fredsterId);
  await page.getByLabel("Surname if not already set").fill("Avenant");
  await page.getByRole("button", { name: "Start private PIN setup" }).click();
  await expect(
    page.getByRole("heading", { name: "Over to Fredster" }),
  ).toBeVisible();
  await page.getByLabel("4-digit PIN").fill("0123");
  await page.getByLabel("Confirm PIN").fill("0123");
  await page.getByRole("button", { name: "Save PIN and open board" }).click();

  await page.getByRole("button", { name: "Mark as done" }).click();
  await expect(page.getByText("Sent to a grown-up for approval")).toBeVisible();
  await page.locator(".identity-menu summary").click();
  await page.getByRole("button", { name: "Sign out" }).click();

  await page.getByRole("combobox").selectOption(addieId);
  await page.getByLabel("6-digit PIN").fill("012345");
  await page.getByRole("button", { name: "Open my board" }).click();
  await page.getByRole("button", { name: "Approve +3 points" }).click();
  await expect(page.getByText("Approved — 3 points awarded")).toBeVisible();
});

test("an expired remembered session returns to the chooser", async ({
  page,
}) => {
  let firstRefresh = true;
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (!path.startsWith("/api/")) {
      return route.continue();
    }
    if (path === "/api/auth/refresh") {
      if (firstRefresh) {
        firstRefresh = false;
        return json(route, auth(addieId, "Addie", "adult"));
      }
      return problem(route, 401);
    }
    if (path === "/api/auth/start") {
      return json(route, {
        state: "signIn",
        members: [member(addieId, "Addie", "adult")],
      });
    }
    if (path === "/api/today") {
      return json(route, board("Addie", true, "open"));
    }
    return problem(route, 404);
  });

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Good day, Addie!" }),
  ).toBeVisible();
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Who’s using the board?" }),
  ).toBeVisible();
});

function member(id: string, displayName: string, role: "adult" | "child") {
  return { id, displayName, role, requiresSurname: true };
}

function auth(id: string, displayName: string, role: "adult" | "child") {
  return {
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id, displayName, role },
  };
}

function board(
  displayName: string,
  isAdult: boolean,
  status: "open" | "pendingApproval" | "approved",
) {
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
    jobs: [job(status)],
    pointsBalance: isAdult ? null : status === "approved" ? 3 : 0,
    pointEarnings: [],
    pendingApprovalCount: status === "pendingApproval" ? 1 : 0,
  };
}

function job(status: "open" | "pendingApproval" | "approved") {
  return {
    id: "7009b529-733c-4770-ae56-1f6fa69f6363",
    childId: fredsterId,
    childDisplayName: "Fredster",
    name: "Feed the dog",
    description: "Fill the food bowl.",
    points: 3,
    scheduledDate: "2026-09-06",
    agendaPeriod: "morning",
    scheduledTime: null,
    recurringJobSeriesId: null,
    recurrenceFrequency: null,
    status,
    completedAtUtc: status === "open" ? null : "2026-09-06T08:00:00Z",
    approvedAtUtc: status === "approved" ? "2026-09-06T08:05:00Z" : null,
    latestRejection: null,
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
