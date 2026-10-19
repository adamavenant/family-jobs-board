import { expect, test } from "@playwright/test";
import type { Route } from "@playwright/test";

const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";
const albaId = "c8318644-906d-420c-89f4-d5672e2c9b71";
const benId = "38692edf-3474-45a5-b979-5fb206f34c59";

test("an adult runs several whose-turn rotations side by side", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1024, height: 900 });
  interface Saved {
    rotationId: string;
    question: string;
    order: string[];
    first: string;
    ended?: boolean;
  }
  const saved: Saved[] = [];
  let signedIn = false;
  const names: Record<string, string> = { [albaId]: "Alba", [benId]: "Ben" };

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (!path.startsWith("/api/")) {
      return route.continue();
    }

    if (path === "/api/auth/refresh") {
      return signedIn
        ? json(route, auth(addieId, "Addie", "adult"))
        : problem(route, 401);
    }
    if (path === "/api/auth/start") {
      return json(route, {
        state: "signIn",
        members: [member(addieId, "Addie", "adult")],
      });
    }
    if (path === "/api/auth/sign-in") {
      signedIn = true;
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
    if (
      path.startsWith("/api/turn-rotations/") &&
      request.method() === "DELETE"
    ) {
      const rotation = saved.find(
        (item) => item.rotationId === path.split("/")[3],
      );
      if (!rotation) return problem(route, 404);
      rotation.ended = true;
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
    page.getByText("Whose Turn Is It? rotation saved."),
  ).toBeVisible();

  const second = page.getByRole("form", { name: "Add a rotation" });
  await second.getByLabel("Alba").check();
  await second.getByLabel("Ben").check();
  await second.getByLabel("First turn").selectOption(benId);
  await second.getByLabel("Question (optional)").fill("Who sits next to Mum?");
  await second.getByRole("button", { name: "Add rotation" }).click();

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

  await page.goto("/");
  await page.getByText("Manage rotations").click();
  await page
    .getByRole("button", { name: "End Who is Pink today?", exact: true })
    .click();
  await page.getByRole("button", { name: "Yes, end it" }).click();
  await expect(
    page.getByText("Ends tomorrow. Today’s answer stays as it is."),
  ).toBeVisible();
  await expect(page.locator(".whose-turn-card")).toHaveCount(2);
  await expect(
    page.getByRole("button", { name: "End Who is Pink today?", exact: true }),
  ).toHaveCount(0);
  await page.reload();
  await page.getByText("Manage rotations").click();
  await expect(
    page.getByText("Ends tomorrow. Today’s answer stays as it is."),
  ).toBeVisible();
  await page.getByRole("link", { name: "Next day →" }).click();
  await expect(page.locator(".whose-turn-card")).toHaveCount(1);
  await expect(page.locator(".whose-turn-card")).toContainText(
    "Who sits next to Mum?",
  );
  for (const width of [768, 390]) {
    await page.setViewportSize({ width, height: 900 });
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth,
      ),
    ).toBe(true);
  }

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
      whoseTurns: saved
        .filter((rotation) => !rotation.ended || date === "2026-09-21")
        .map((rotation) => turnFor(rotation, date)),
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
        upcomingTurns: (rotation.ended
          ? ["2026-09-21"]
          : ["2026-09-21", "2026-09-22"]
        ).map((date) => ({ ...turnFor(rotation, date), date })),
      })),
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
