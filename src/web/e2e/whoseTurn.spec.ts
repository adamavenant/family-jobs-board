import { expect, test } from "@playwright/test";
import type { Page, Route } from "@playwright/test";
import type { components } from "../src/api/schema";

type MemberResponse = components["schemas"]["MemberResponse"];
type TodayResponse = components["schemas"]["TodayResponse"];
type WhoseTurnResponse = components["schemas"]["WhoseTurnResponse"];

import { saveQueuedAction } from "./undo";

const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";
const albaId = "c8318644-906d-420c-89f4-d5672e2c9b71";
const benId = "38692edf-3474-45a5-b979-5fb206f34c59";
const cassId = "0b7d8f8e-5d0a-4e7e-9a55-2f6c1d3b9e41";
const showerId = "33333333-3333-4333-8333-333333333333";
const movieId = "55555555-5555-4555-8555-555555555555";

for (const viewport of [
  { name: "phone", width: 375, height: 812 },
  { name: "tablet", width: 768, height: 1024 },
]) {
  test(`a child ticks off turns that don't happen every day on a ${viewport.name}`, async ({
    page,
  }) => {
    await page.setViewportSize({
      width: viewport.width,
      height: viewport.height,
    });
    const showerOrder = [albaId, benId];
    const showerTicks: string[] = [];
    const showerTickBodies: unknown[] = [];
    let movieTaken = false;
    const names: Record<string, string> = {
      [albaId]: "Alba",
      [benId]: "Ben",
      [cassId]: "Cass",
    };

    await page.route("**/api/**", async (route) => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      if (!path.startsWith("/api/")) {
        return route.continue();
      }
      if (path === "/api/auth/refresh") {
        return json(route, auth(albaId, "Alba", "child"));
      }
      if (path === "/api/today") {
        return json(route, board());
      }
      if (path === `/api/turn-rotations/${showerId}/turns`) {
        showerTickBodies.push(request.postDataJSON());
        showerTicks.push(request.postDataJSON().childId);
        return json(route, taken(showerId, `shower-${showerTicks.length}`));
      }
      if (path.startsWith(`/api/turn-rotations/${showerId}/turns/`)) {
        showerTicks.pop();
        return json(route, taken(showerId, "undone"));
      }
      if (path === `/api/turn-rotations/${movieId}/turns`) {
        movieTaken = true;
        return json(route, taken(movieId, "movie"));
      }
      if (path.startsWith(`/api/turn-rotations/${movieId}/turns/`)) {
        movieTaken = false;
        return json(route, taken(movieId, "movie"));
      }
      return problem(route, 404);
    });

    await page.goto("/");
    await expect(
      page.getByRole("heading", { name: "Good day, Alba!" }),
    ).toBeVisible();

    const shower = page.locator(".whose-turn-card").filter({
      hasText: "Who is first in the shower?",
    });
    const movie = page.locator(".whose-turn-card").filter({
      hasText: "Who picks the movie?",
    });
    await expect(shower.locator(".whose-turn-card__child")).toHaveText(/Alba/);
    await expectNoSidewaysScroll(page);

    // With two children the checkbox passes straight to the other child, so it does not
    // stay ticked.
    await shower
      .getByRole("checkbox", { name: "Alba has had their turn" })
      .click();
    // The tick waits behind the undo toast, shown as done, until it is saved.
    await expect(
      shower.getByRole("checkbox", { name: "Alba has had their turn" }),
    ).toBeChecked();
    expect(showerTickBodies).toEqual([]);
    await saveQueuedAction(page);
    await expect(shower.locator(".whose-turn-card__child")).toHaveText(/Ben/);
    expect(showerTickBodies).toEqual([
      {
        requestId: expect.any(String),
        revisionId: "66666666-6666-4666-8666-666666666666",
        turnNumber: 0,
        childId: albaId,
      },
    ]);
    await expect(shower.getByRole("checkbox")).toHaveCount(0);
    await expect(shower.getByText("Alba just had a turn.")).toBeVisible();
    await expectNoSidewaysScroll(page);

    await shower.getByRole("button", { name: "Undo Alba’s turn" }).click();
    await saveQueuedAction(page);
    await expect(shower.locator(".whose-turn-card__child")).toHaveText(/Alba/);
    await expect(
      shower.getByRole("checkbox", { name: "Alba has had their turn" }),
    ).not.toBeChecked();

    const movieTick = movie.getByRole("checkbox", {
      name: "Alba has had their turn",
    });
    await movieTick.focus();
    await page.keyboard.press("Space");
    await expect(movie.locator(".whose-turn-card__taken")).toBeVisible();
    await expect(movieTick).toBeChecked();
    await expectNoSidewaysScroll(page);

    function board(): TodayResponse {
      const alba = child(albaId, "Alba");
      const showerChild = showerOrder[showerTicks.length % 2]!;
      const lastShowerTick = showerTicks.at(-1);
      const showerTurn: WhoseTurnResponse = {
        rotationId: showerId,
        question: "Who is first in the shower?",
        childId: showerChild,
        childDisplayName: names[showerChild]!,
        revisionId: "66666666-6666-4666-8666-666666666666",
        turnNumber: showerTicks.length,
        advanceMode: "whenTicked",
        turnTaken: false,
        canTakeTurn: showerChild === albaId,
        undoableTurn:
          lastShowerTick === albaId
            ? {
                turnId: `shower-${showerTicks.length}`,
                childId: albaId,
                childDisplayName: "Alba",
              }
            : null,
      };
      const movieTurn: WhoseTurnResponse = {
        rotationId: movieId,
        question: "Who picks the movie?",
        childId: albaId,
        childDisplayName: "Alba",
        revisionId: "77777777-7777-4777-8777-777777777777",
        turnNumber: 0,
        advanceMode: "whenTicked",
        turnTaken: movieTaken,
        canTakeTurn: !movieTaken,
        undoableTurn: movieTaken
          ? { turnId: "movie", childId: albaId, childDisplayName: "Alba" }
          : null,
      };
      return {
        viewer: alba,
        members: [alba, child(benId, "Ben"), child(cassId, "Cass")],
        date: "2026-09-21",
        currentDate: "2026-09-21",
        selectedChildId: null,
        jobs: [],
        pointsBalance: 0,
        pendingApprovalCount: 0,
        whoseTurns: [showerTurn, movieTurn],
      };
    }

    function taken(rotationId: string, id: string) {
      return {
        id,
        requestId: "88888888-8888-4888-8888-888888888888",
        rotationId,
        revisionId: "66666666-6666-4666-8666-666666666666",
        turnNumber: 0,
        date: "2026-09-21",
        childId: albaId,
        childDisplayName: "Alba",
        recordedByMemberId: albaId,
        recordedAtUtc: "2026-09-21T08:00:00Z",
        undoneByMemberId: null,
        undoneAtUtc: null,
      };
    }
  });
}

test("an adult runs several whose-turn rotations side by side", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  interface Saved {
    rotationId: string;
    question: string;
    order: string[];
    first: string;
  }
  const saved: Saved[] = [];
  const names: Record<string, string> = { [albaId]: "Alba", [benId]: "Ben" };

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
    if (path === "/api/turn-rotations" && request.method() === "GET") {
      return json(route, overview());
    }
    if (path === "/api/turn-rotations" && request.method() === "POST") {
      const body = request.postDataJSON();
      saved.push({
        rotationId: `00000000-0000-4000-8000-00000000000${saved.length + 1}`,
        question: body.question ?? "Who is Pink today?",
        order: body.participantChildIds,
        first: body.firstChildId,
      });
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

  await page.getByText("Manage rotations").click();
  const first = page.getByRole("form", { name: "Add a rotation" });
  await first.getByLabel("Alba").check();
  await first.getByLabel("Ben").check();
  await first.getByLabel("First turn").selectOption(albaId);
  await first.getByRole("button", { name: "Add rotation" }).click();
  await expect(
    page
      .locator(".toast-stack")
      .getByText("Started “Who is Pink today?” from 2026-09-21"),
  ).toBeVisible();
  await saveQueuedAction(page);

  const second = page.getByRole("form", { name: "Add a rotation" });
  await second.getByLabel("Alba").check();
  await second.getByLabel("Ben").check();
  await second.getByLabel("First turn").selectOption(benId);
  await second.getByLabel("Question (optional)").fill("Who sits next to Mum?");
  await second.getByRole("button", { name: "Add rotation" }).click();
  await saveQueuedAction(page);

  const pink = page.locator(".whose-turn-card").filter({
    hasText: "Who is Pink today?",
  });
  const mum = page.locator(".whose-turn-card").filter({
    hasText: "Who sits next to Mum?",
  });
  await expect(pink.locator(".whose-turn-card__child")).toContainText("Alba");
  await expect(mum.locator(".whose-turn-card__child")).toContainText("Ben");

  await page.getByRole("link", { name: "Next day →" }).click();

  const pinkTomorrow = page.locator(".whose-turn-card").filter({
    hasText: "Who is Pink on Tuesday?",
  });
  const mumTomorrow = page.locator(".whose-turn-card").filter({
    hasText: "Who sits next to Mum? (Tuesday)",
  });
  await expect(pinkTomorrow.locator(".whose-turn-card__child")).toContainText(
    "Ben",
  );
  await expect(mumTomorrow.locator(".whose-turn-card__child")).toContainText(
    "Alba",
  );

  function turnFor(rotation: Saved, date: string) {
    const offset = date === "2026-09-22" ? 1 : 0;
    const start = rotation.order.indexOf(rotation.first);
    const childId = rotation.order[(start + offset) % rotation.order.length]!;
    return {
      rotationId: rotation.rotationId,
      question: rotation.question,
      childId,
      childDisplayName: names[childId],
    };
  }

  function board(date: string) {
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
      pendingApprovalCount: 0,
      whoseTurns: saved.map((rotation) => turnFor(rotation, date)),
    };
  }

  function overview() {
    return {
      rotations: saved.map((rotation) => ({
        rotationId: rotation.rotationId,
        current: {
          id: "11111111-1111-4111-8111-111111111111",
          effectiveFrom: "2026-09-21",
          question: rotation.question,
          participantChildIds: rotation.order,
          firstChildId: rotation.first,
          createdByMemberId: addieId,
          createdAtUtc: "2026-09-21T08:00:00Z",
        },
        upcomingTurns: [
          {
            ...turnFor(rotation, "2026-09-21"),
            date: "2026-09-21",
          },
        ],
      })),
    };
  }
});

function child(id: string, displayName: string): MemberResponse {
  return {
    id,
    firstName: displayName,
    nickname: null,
    displayName,
    isAdult: false,
  };
}

async function expectNoSidewaysScroll(page: Page) {
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
}

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
