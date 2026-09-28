import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { acceptSession, clearIdentity } from "../../api/auth";
import { routes } from "../../app/routes";

const addie = member("22eb0cc1-058e-4b2e-bb18-d7aaad564a6c", "Addie", true);
const fredster = member(
  "754de05d-b6f6-4626-bbad-79e2079cc5c3",
  "Fredster",
  false,
);
const harrie = member("e22facf5-69ce-45ce-9dad-306eef1852c9", "Harrie", false);
const annaId = "4a4b3b0e-8d1f-4c55-a6f0-3d2c1b0a9e88";

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
  window.localStorage.clear();
});

describe("Points ledger", () => {
  it("lets a child open their own points from the total on their board", async () => {
    const api = fakeApi({
      board: board(fredster, { pointsBalance: 4 }),
      ledgers: [
        ledger({
          selectedChildId: fredster.id,
          children: [child(fredster, 4)],
          entries: [
            entry(fredster, "Broke a plate", -2, 4, "2026-09-21T09:00:00Z"),
            entry(fredster, "Feed the dog", 6, 6, "2026-09-20T08:00:00Z"),
          ],
        }),
      ],
    });
    const user = userEvent.setup();
    renderApp(fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    expect(
      screen.queryByRole("heading", { name: "How Fredster earned them" }),
    ).not.toBeInTheDocument();
    screen
      .getByRole("link", { name: "4 points earned. See how you earned them" })
      .focus();
    await user.keyboard("{Enter}");

    await screen.findByRole("heading", { name: "Your points" });
    expect(screen.getByLabelText("4 points earned")).toBeInTheDocument();
    expect(
      screen.queryByRole("navigation", { name: "Filter points by child" }),
    ).not.toBeInTheDocument();
    const list = screen.getByRole("list", { name: "How Fredster earned them" });
    const [newest, oldest] = within(list).getAllByRole("listitem");
    expect(newest).toHaveTextContent("−2");
    expect(newest).toHaveTextContent("Broke a plate");
    expect(newest).toHaveTextContent("Balance 4");
    expect(within(newest as HTMLElement).getByText(/2026/)).toHaveAttribute(
      "datetime",
      "2026-09-21T09:00:00Z",
    );
    expect(oldest).toHaveTextContent("+6");
    expect(oldest).toHaveTextContent("Feed the dog");
    expect(oldest).toHaveTextContent("Balance 6");
    // A child's own ledger never names children, sources, or adults.
    expect(list).not.toHaveTextContent("Fredster");
    expect(list).not.toHaveTextContent(/Adjustment|Job|logged by/);
    expect(api.ledgerRequests).toEqual([{ childId: null, before: null }]);
  });

  it("ignores a child filter in the URL when a child opens the page", async () => {
    const api = fakeApi({
      ledgers: [
        ledger({
          selectedChildId: fredster.id,
          children: [child(fredster, 0)],
        }),
      ],
    });
    renderApp(fredster, `/points?childId=${harrie.id}`);

    expect(
      await screen.findByText(
        "No points earned yet. Complete and approve a job, or show a good behaviour, to start the list.",
      ),
    ).toBeInTheDocument();
    expect(api.ledgerRequests).toEqual([{ childId: null, before: null }]);
  });

  it("shows adults every child's points and filters to one child", async () => {
    const api = fakeApi({
      board: board(addie, { pointsBalance: null }),
      ledgers: [
        ledger({
          children: [child(fredster, 12), child(harrie, 7), inactiveAnna(-3)],
          entries: [
            entry(harrie, "Being Brave", 7, 7, "2026-09-21T10:00:00Z"),
            entry(fredster, "Feed the dog", 12, 12, "2026-09-21T09:00:00Z"),
          ],
        }),
        ledger({
          selectedChildId: harrie.id,
          children: [child(fredster, 12), child(harrie, 7), inactiveAnna(-3)],
          entries: [entry(harrie, "Being Brave", 7, 7, "2026-09-21T10:00:00Z")],
        }),
      ],
    });
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByRole("link", { name: "Points" }));

    await screen.findByRole("heading", { name: "Every child’s points" });
    const filter = screen.getByRole("navigation", {
      name: "Filter points by child",
    });
    expect(
      within(filter).getByRole("link", {
        name: "All children",
        current: "page",
      }),
    ).toBeInTheDocument();
    expect(
      within(filter).getByRole("link", { name: "Fredster 12 points" }),
    ).toBeInTheDocument();
    expect(
      within(filter).getByRole("link", { name: "Anna (inactive) −3 points" }),
    ).toBeInTheDocument();
    const allList = screen.getByRole("list", { name: "Every child’s points" });
    const [harrieEntry, fredsterEntry] =
      within(allList).getAllByRole("listitem");
    expect(harrieEntry).toHaveTextContent("Harrie");
    expect(fredsterEntry).toHaveTextContent("Fredster");

    // Keyboard users keep their place in the filter after choosing a child.
    const harrieLink = within(filter).getByRole("link", {
      name: "Harrie 7 points",
    });
    harrieLink.focus();
    await user.keyboard("{Enter}");

    await screen.findByRole("heading", { name: "Harrie’s points" });
    expect(harrieLink).toHaveAttribute("aria-current", "page");
    expect(harrieLink).toHaveFocus();
    const harrieList = screen.getByRole("list", { name: "Harrie’s points" });
    expect(within(harrieList).getByRole("listitem")).toHaveTextContent(
      "Being Brave",
    );
    expect(harrieList).not.toHaveTextContent("Harrie");
    expect(api.ledgerRequests).toEqual([
      { childId: null, before: null },
      { childId: harrie.id, before: null },
    ]);
  });

  it("appends older entries on request until there are none left", async () => {
    const firstPage = Array.from({ length: 20 }, (_, index) =>
      entry(
        fredster,
        `Newer ${index}`,
        1,
        40 - index,
        `2026-09-21T${String(10 + (index % 10)).padStart(2, "0")}:00:00Z`,
      ),
    );
    const api = fakeApi({
      ledgers: [
        ledger({
          selectedChildId: fredster.id,
          children: [child(fredster, 12), child(harrie, 0)],
          entries: firstPage,
          nextCursor: "cursor-1",
        }),
        { status: 500 },
        ledger({
          selectedChildId: fredster.id,
          children: [child(fredster, 12), child(harrie, 0)],
          entries: [
            entry(fredster, "Older 1", 1, 20, "2026-09-01T08:00:00Z"),
            entry(fredster, "Older 2", 19, 19, "2026-09-01T07:00:00Z"),
          ],
        }),
      ],
    });
    const user = userEvent.setup();
    renderApp(addie, `/points?childId=${fredster.id}`);

    const list = await screen.findByRole("list", {
      name: "Fredster’s points",
    });
    expect(within(list).getAllByRole("listitem")).toHaveLength(20);

    await user.click(
      screen.getByRole("button", { name: "Show older entries" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "We couldn't load the points.",
    );
    expect(within(list).getAllByRole("listitem")).toHaveLength(20);

    await user.click(
      screen.getByRole("button", { name: "Show older entries" }),
    );
    await screen.findByText("Older 2");
    const items = within(list).getAllByRole("listitem");
    expect(items).toHaveLength(22);
    expect(items[21]).toHaveTextContent("Balance 19");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Show older entries" }),
    ).not.toBeInTheDocument();
    expect(api.ledgerRequests).toEqual([
      { childId: fredster.id, before: null },
      { childId: fredster.id, before: "cursor-1" },
      { childId: fredster.id, before: "cursor-1" },
    ]);
  });

  it("explains when there are no points yet", async () => {
    fakeApi({
      ledgers: [
        ledger({ children: [child(fredster, 0), child(harrie, 0)] }),
        ledger({
          selectedChildId: harrie.id,
          children: [child(fredster, 0), child(harrie, 0)],
        }),
      ],
    });
    const user = userEvent.setup();
    renderApp(addie, "/points");

    expect(
      await screen.findByText("No points have been earned yet."),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("link", { name: "Harrie 0 points" }));
    expect(
      await screen.findByText("Harrie hasn’t earned any points yet."),
    ).toBeInTheDocument();
  });

  it("shows the error with a way back when the points can't load", async () => {
    fakeApi({ ledgers: [{ status: 500 }] });
    renderApp(addie, "/points");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "We couldn't load the points.",
    );
    expect(
      screen.getByRole("link", { name: "← Back to today" }),
    ).toHaveAttribute("href", "/");
  });

  it("signs out from the points page", async () => {
    const api = fakeApi({
      ledgers: [ledger({ children: [child(fredster, 0)] })],
    });
    const user = userEvent.setup();
    renderApp(addie, "/points");

    await screen.findByRole("heading", { name: "Points" });
    await user.click(screen.getByText("Addie", { selector: "summary" }));
    await user.click(screen.getByRole("button", { name: "Sign out" }));

    expect(
      await screen.findByRole("heading", { name: "Who’s using the board?" }),
    ).toBeInTheDocument();
    expect(api.loggedOut).toBe(true);
  });
});

type Member = ReturnType<typeof member>;

function member(id: string, firstName: string, isAdult: boolean) {
  return { id, firstName, nickname: null, displayName: firstName, isAdult };
}

function child(from: Member, balance: number) {
  return {
    id: from.id,
    displayName: from.displayName,
    isActive: true,
    balance,
  };
}

function inactiveAnna(balance: number) {
  return { id: annaId, displayName: "Anna", isActive: false, balance };
}

function entry(
  from: Member,
  name: string,
  points: number,
  balanceAfter: number,
  awardedAtUtc: string,
) {
  return {
    id: crypto.randomUUID(),
    childId: from.id,
    childDisplayName: from.displayName,
    name,
    points,
    balanceAfter,
    awardedAtUtc,
  };
}

function ledger(overrides: {
  selectedChildId?: string;
  children: ReturnType<typeof child>[];
  entries?: ReturnType<typeof entry>[];
  nextCursor?: string;
}) {
  return {
    selectedChildId: overrides.selectedChildId ?? null,
    children: overrides.children,
    entries: overrides.entries ?? [],
    nextCursor: overrides.nextCursor ?? null,
  };
}

function board(viewer: Member, overrides: { pointsBalance: number | null }) {
  return {
    viewer,
    members: [addie, fredster, harrie],
    date: "2026-09-21",
    currentDate: "2026-09-21",
    selectedChildId: null,
    jobs: [],
    pointsBalance: overrides.pointsBalance,
    pendingApprovalCount: 0,
    whoseTurns: [],
  };
}

/** Serves the board, then each ledger response in order; `{ status }` fails that request. */
function fakeApi(options: {
  board?: unknown;
  ledgers: (ReturnType<typeof ledger> | { status: number })[];
}) {
  const ledgerRequests: { childId: string | null; before: string | null }[] =
    [];
  const state = { loggedOut: false };
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(
        input instanceof Request ? input.url : String(input),
        "http://localhost",
      );
      if (url.pathname === "/api/points-ledger") {
        ledgerRequests.push({
          childId: url.searchParams.get("childId"),
          before: url.searchParams.get("before"),
        });
        const next = options.ledgers[ledgerRequests.length - 1];
        return next && "status" in next
          ? jsonResponse({ title: "Server error" }, { status: next.status })
          : jsonResponse(next);
      }
      if (url.pathname === "/api/auth/logout") {
        state.loggedOut = true;
        return new Response(null, { status: 204 });
      }
      if (url.pathname === "/api/auth/refresh") {
        return new Response(null, { status: 401 });
      }
      if (url.pathname === "/api/auth/start") {
        return jsonResponse({
          state: "signIn",
          members: [
            {
              id: addie.id,
              displayName: "Addie",
              role: "adult",
              requiresSurname: false,
            },
          ],
        });
      }

      return jsonResponse(options.board);
    }),
  );
  return {
    ledgerRequests,
    get loggedOut() {
      return state.loggedOut;
    },
  };
}

function renderApp(viewer: Member, path = "/") {
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
