import { expect, test } from "@playwright/test";
import type { Route } from "@playwright/test";

const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";
const albaId = "c8318644-906d-420c-89f4-d5672e2c9b71";
const benId = "38692edf-3474-45a5-b979-5fb206f34c59";

test("an adult sets up the whose-turn rotation and it updates across days", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  let configured = false;
  let savedQuestion: string | null = null;
  let saveRequestBody: Record<string, unknown> | null = null;

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
      return json(route, {
        state: "signIn",
        members: [member(addieId, "Addie", "adult")],
      });
    }
    if (path === "/api/auth/sign-in") {
      return json(route, auth(addieId, "Addie", "adult"));
    }
    if (path === "/api/turn-rotation" && request.method() === "GET") {
      return json(route, overview());
    }
    if (path === "/api/turn-rotation" && request.method() === "PUT") {
      saveRequestBody = request.postDataJSON();
      configured = true;
      savedQuestion =
        (saveRequestBody.question as string | null) ?? "Who is Pink today?";
      return json(route, overview());
    }
    if (path === "/api/today") {
      const date =
        new URL(request.url()).searchParams.get("date") ?? "2026-09-21";
      return json(route, board(date));
    }
    return problem(route, 404);
  });

  await page.goto("/");
  await page.getByRole("combobox").selectOption(addieId);
  await page.getByLabel("6-digit PIN").fill("012345");
  await page.getByRole("button", { name: "Open my board" }).click();

  await expect(
    page.getByRole("heading", { name: "Good day, Addie!" }),
  ).toBeVisible();
  await expect(
    page.getByText(
      "Whose Turn Is It? isn’t set up yet. Configure it in the tools above.",
    ),
  ).toBeVisible();

  await page.getByText("Set it up").click();
  const form = page.getByRole("form", { name: "Configure whose turn is it" });
  await form.getByLabel("Alba").check();
  await form.getByLabel("Ben").check();
  await form.getByLabel("First turn").selectOption(albaId);
  await form.getByLabel("Question (optional)").fill("Who feeds the fish?");
  await form.getByRole("button", { name: "Set up rotation" }).click();

  await expect(
    page.getByText("Whose Turn Is It? rotation saved."),
  ).toBeVisible();
  expect(configured).toBe(true);
  expect(saveRequestBody).toMatchObject({
    participantChildIds: [albaId, benId],
    firstChildId: albaId,
    effectiveFrom: "2026-09-21",
    question: "Who feeds the fish?",
  });

  const card = page.locator(".whose-turn-card").filter({
    hasText: "Who feeds the fish?",
  });
  await expect(card).toBeVisible();
  await expect(card.locator(".whose-turn-card__child")).toContainText("Alba");

  await page.getByRole("link", { name: "Next day →" }).click();

  const tomorrowCard = page.locator(".whose-turn-card").filter({
    hasText: "Who feeds the fish? (Tuesday)",
  });
  await expect(tomorrowCard).toBeVisible();
  await expect(tomorrowCard.locator(".whose-turn-card__child")).toContainText(
    "Ben",
  );

  function board(date: string) {
    const whoseTurn = configured
      ? date === "2026-09-22"
        ? { question: savedQuestion, childId: benId, childDisplayName: "Ben" }
        : { question: savedQuestion, childId: albaId, childDisplayName: "Alba" }
      : null;
    return {
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
          id: albaId,
          firstName: "Alba",
          nickname: null,
          displayName: "Alba",
          isAdult: false,
        },
        {
          id: benId,
          firstName: "Ben",
          nickname: null,
          displayName: "Ben",
          isAdult: false,
        },
      ],
      date,
      currentDate: "2026-09-21",
      selectedChildId: null,
      jobs: [],
      pointsBalance: null,
      pointEarnings: [],
      pendingApprovalCount: 0,
      whoseTurn,
    };
  }

  function overview() {
    if (!configured) {
      return { current: null, upcomingTurns: [] };
    }

    return {
      current: {
        id: "11111111-1111-1111-1111-111111111111",
        effectiveFrom: "2026-09-21",
        question: savedQuestion,
        participantChildIds: [albaId, benId],
        firstChildId: albaId,
        createdByMemberId: addieId,
        createdAtUtc: "2026-09-21T08:00:00Z",
      },
      upcomingTurns: [
        { date: "2026-09-21", question: savedQuestion, childId: albaId },
        { date: "2026-09-22", question: savedQuestion, childId: benId },
      ],
    };
  }
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
