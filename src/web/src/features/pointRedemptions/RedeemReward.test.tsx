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

const adultBoard = {
  viewer: addie,
  members: [addie, fredster, harrie],
  date: "2026-10-05",
  currentDate: "2026-10-05",
  selectedChildId: null,
  pointsBalance: null,
  pendingApprovalCount: 0,
  jobs: [],
};

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
  window.localStorage.clear();
});

interface RedemptionRequest {
  requestId: string;
  childId: string;
  points: number;
  reward: string;
}

describe("Redeem a reward", () => {
  it("lets an adult spend a child's points on a reward", async () => {
    const api = fakeApi({
      balances: { [fredster.id]: 12, [harrie.id]: 3 },
      onRedeem: (body) => redeemed(body, 7),
    });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.selectOptions(within(form).getByLabelText("Child"), "Fredster");

    expect(
      await within(form).findByText("Fredster has 12 points to spend."),
    ).toBeInTheDocument();
    expect(within(form).getByLabelText("Points to spend")).toHaveAttribute(
      "max",
      "12",
    );
    await user.type(within(form).getByLabelText("Points to spend"), "5");
    await user.type(within(form).getByLabelText("Reward"), " Ice cream ");
    await user.click(
      within(form).getByRole("button", { name: "Redeem points" }),
    );

    expect(
      await screen.findByText(
        "Fredster spent 5 points on Ice cream. Balance is now 7.",
      ),
    ).toHaveAttribute("role", "status");
    expect(api.redemptions).toEqual([
      {
        requestId: expect.any(String),
        childId: fredster.id,
        points: 5,
        reward: "Ice cream",
      },
    ]);
    expect(within(form).getByLabelText("Child")).toHaveValue("");
    expect(within(form).getByLabelText("Reward")).toHaveValue("");
  });

  it("asks for a child rather than picking one", async () => {
    fakeApi({ balances: { [fredster.id]: 12 } });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);

    expect(within(form).getByLabelText("Child")).toHaveValue("");
    expect(
      within(form).getByRole("option", { name: "Choose a child" }),
    ).toBeInTheDocument();
    expect(within(form).queryByText(/has .* to spend/)).not.toBeInTheDocument();
  });

  it("shows the server's message when the child doesn't have enough points and reuses the request ID", async () => {
    let attempt = 0;
    const api = fakeApi({
      balances: { [fredster.id]: 4 },
      onRedeem: (body) => {
        attempt += 1;
        return attempt === 1 ? insufficientPoints(4) : redeemed(body, 0);
      },
    });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.selectOptions(within(form).getByLabelText("Child"), "Fredster");
    await within(form).findByText("Fredster has 4 points to spend.");
    // The balance shown can be out of date; the server has the final say.
    await user.type(within(form).getByLabelText("Points to spend"), "4");
    await user.type(within(form).getByLabelText("Reward"), "Movie night");
    const submit = within(form).getByRole("button", { name: "Redeem points" });
    await user.click(submit);

    expect(await within(form).findByRole("alert")).toHaveTextContent(
      "Fredster only has 4 points.",
    );
    expect(screen.queryByText(/Balance is now/)).not.toBeInTheDocument();

    await user.click(submit);
    await screen.findByText(
      "Fredster spent 4 points on Movie night. Balance is now 0.",
    );
    const [failed, retried] = api.redemptions;
    expect(retried?.requestId).toBe(failed?.requestId);
  });

  it("explains when a child has no points and won't submit", async () => {
    fakeApi({ balances: { [fredster.id]: 12, [harrie.id]: 0 } });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.selectOptions(within(form).getByLabelText("Child"), "Harrie");

    expect(
      await within(form).findByText(
        "Harrie doesn't have any points to spend yet.",
      ),
    ).toBeInTheDocument();
    expect(
      within(form).getByRole("button", { name: "Redeem points" }),
    ).toBeDisabled();
  });

  it("still lets the adult redeem when the balances can't load", async () => {
    const api = fakeApi({
      balances: null,
      onRedeem: (body) => redeemed(body, 2),
    });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.selectOptions(within(form).getByLabelText("Child"), "Harrie");

    expect(
      await within(form).findByText(/The balance couldn’t be loaded/),
    ).toBeInTheDocument();
    await user.type(within(form).getByLabelText("Points to spend"), "1");
    await user.type(within(form).getByLabelText("Reward"), "Sticker");
    await user.click(
      within(form).getByRole("button", { name: "Redeem points" }),
    );

    await screen.findByText(
      "Harrie spent 1 point on Sticker. Balance is now 2.",
    );
    expect(api.redemptions).toHaveLength(1);
  });

  it("reloads the balances each time the panel opens", async () => {
    const api = fakeApi({ balances: { [fredster.id]: 0 } });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.selectOptions(within(form).getByLabelText("Child"), "Fredster");
    await within(form).findByText(
      "Fredster doesn't have any points to spend yet.",
    );
    expect(
      within(form).getByRole("button", { name: "Redeem points" }),
    ).toBeDisabled();

    // Fredster earns points somewhere else while the panel is closed.
    api.setBalances({ [fredster.id]: 6 });
    await reopenPanel(user);

    expect(
      await within(form).findByText("Fredster has 6 points to spend."),
    ).toBeInTheDocument();
    expect(
      within(form).getByRole("button", { name: "Redeem points" }),
    ).toBeEnabled();
    expect(within(form).getByLabelText("Points to spend")).toHaveAttribute(
      "max",
      "6",
    );
  });

  it("retries a failed balance load when the panel is reopened", async () => {
    const api = fakeApi({ balances: null });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.selectOptions(within(form).getByLabelText("Child"), "Harrie");
    await within(form).findByText(/The balance couldn’t be loaded/);

    api.setBalances({ [harrie.id]: 3 });
    await reopenPanel(user);

    expect(
      await within(form).findByText("Harrie has 3 points to spend."),
    ).toBeInTheDocument();
  });

  it("does not show children the redeem tool", async () => {
    fakeApi({
      board: { ...adultBoard, viewer: fredster, pointsBalance: 4 },
      balances: {},
    });
    renderApp(fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    expect(
      screen.queryByRole("form", { name: "Redeem a reward" }),
    ).not.toBeInTheDocument();
    expect(document.querySelector(".point-redemptions")).toBeNull();
  });
});

function redeemed(body: RedemptionRequest, balance: number) {
  return jsonResponse(
    {
      redemption: {
        id: crypto.randomUUID(),
        childId: body.childId,
        redeemedByMemberId: addie.id,
        points: body.points,
        reward: body.reward,
        redeemedAtUtc: "2026-10-05T08:00:00Z",
      },
      pointsBalance: balance,
    },
    { status: 201 },
  );
}

function insufficientPoints(currentBalance: number) {
  return jsonResponse(
    {
      title: "Not enough points",
      detail: `Fredster only has ${currentBalance} points.`,
      status: 409,
      code: "insufficientPoints",
      currentBalance,
    },
    { status: 409 },
  );
}

async function reopenPanel(user: ReturnType<typeof userEvent.setup>) {
  const summary = document.querySelector(".point-redemptions > summary");
  if (!summary) {
    throw new Error("The redeem a reward tool was not rendered.");
  }

  await user.click(summary);
  expect(
    document.querySelector<HTMLDetailsElement>(".point-redemptions")?.open,
  ).toBe(false);
  await user.click(summary);
}

async function openPanel(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole("heading", { name: "Good day, Addie!" });
  const summary = document.querySelector(".point-redemptions > summary");
  if (!summary) {
    throw new Error("The redeem a reward tool was not rendered.");
  }

  await user.click(summary);
  return screen.getByRole("form", { name: "Redeem a reward" });
}

function fakeApi(options: {
  board?: unknown;
  balances: Record<string, number> | null;
  onRedeem?: (body: RedemptionRequest) => Response;
}) {
  const redemptions: RedemptionRequest[] = [];
  let balances = options.balances;
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      const path = new URL(request.url).pathname;
      if (path === "/api/point-redemptions" && request.method === "POST") {
        const body = (await request.clone().json()) as RedemptionRequest;
        redemptions.push(body);
        return (options.onRedeem ?? ((value) => redeemed(value, 0)))(body);
      }

      if (path === "/api/points-ledger") {
        return balances === null
          ? jsonResponse({ title: "Server error" }, { status: 500 })
          : jsonResponse({
              selectedChildId: null,
              children: Object.entries(balances).map(([id, balance]) => ({
                id,
                displayName: id === fredster.id ? "Fredster" : "Harrie",
                isActive: true,
                balance,
              })),
              entries: [],
              nextCursor: null,
            });
      }

      return jsonResponse(options.board ?? adultBoard);
    }),
  );
  return {
    redemptions,
    setBalances(next: Record<string, number> | null) {
      balances = next;
    },
  };
}

function renderApp(viewer: ReturnType<typeof member>) {
  acceptSession({
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: {
      id: viewer.id,
      displayName: viewer.displayName,
      role: viewer.isAdult ? "adult" : "child",
    },
  });
  const router = createMemoryRouter(routes, { initialEntries: ["/"] });
  return render(<RouterProvider router={router} />);
}

function member(id: string, firstName: string, isAdult: boolean) {
  return { id, firstName, nickname: null, displayName: firstName, isAdult };
}

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
}
