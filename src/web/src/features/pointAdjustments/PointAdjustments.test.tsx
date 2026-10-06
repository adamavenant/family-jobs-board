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
  date: "2026-09-21",
  currentDate: "2026-09-21",
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

interface AdjustmentRequest {
  requestId: string;
  childId: string;
  amount: number;
  reason: string;
}

describe("Point adjustments", () => {
  it("lets an adult add points with a reason", async () => {
    const api = fakeApi(adultBoard, (body) => recorded(body, 12));
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.selectOptions(within(form).getByLabelText("Child"), "Harrie");
    await user.type(within(form).getByLabelText("Number of points"), "5");
    await user.type(within(form).getByLabelText("Reason"), " Helped a friend ");
    await user.click(
      within(form).getByRole("button", { name: "Record adjustment" }),
    );

    expect(
      await screen.findByText("Added 5 points for Harrie. Balance is now 12."),
    ).toHaveAttribute("role", "status");
    expect(api.requests).toEqual([
      {
        requestId: expect.any(String),
        childId: harrie.id,
        amount: 5,
        reason: "Helped a friend",
      },
    ]);
    expect(within(form).getByLabelText("Reason")).toHaveValue("");
  });

  it("sends a negative amount when removing points", async () => {
    const api = fakeApi(adultBoard, (body) => recorded(body, 7));
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.click(within(form).getByLabelText("Remove points"));
    await user.type(within(form).getByLabelText("Number of points"), "3");
    await user.type(within(form).getByLabelText("Reason"), "Broke a plate");
    await user.click(
      within(form).getByRole("button", { name: "Record adjustment" }),
    );

    expect(
      await screen.findByText(
        "Removed 3 points from Fredster. Balance is now 7.",
      ),
    ).toBeInTheDocument();
    expect(api.requests[0]).toMatchObject({
      childId: fredster.id,
      amount: -3,
    });
  });

  it("asks for a reason and sends nothing when it is blank", async () => {
    const api = fakeApi(adultBoard, (body) => recorded(body, 1));
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.type(within(form).getByLabelText("Number of points"), "3");
    await user.type(within(form).getByLabelText("Reason"), "   ");
    await user.click(
      within(form).getByRole("button", { name: "Record adjustment" }),
    );

    expect(await within(form).findByRole("alert")).toHaveTextContent(
      "Enter a reason.",
    );
    expect(api.requests).toHaveLength(0);
  });

  it("shows the server's message when removing more points than the child has", async () => {
    const api = fakeApi(adultBoard, () => insufficientPoints(3));
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.click(within(form).getByLabelText("Remove points"));
    await user.type(within(form).getByLabelText("Number of points"), "5");
    await user.type(within(form).getByLabelText("Reason"), "Lost a book");
    await user.click(
      within(form).getByRole("button", { name: "Record adjustment" }),
    );

    expect(await within(form).findByRole("alert")).toHaveTextContent(
      "Fredster only has 3 points.",
    );
    expect(screen.queryByText(/Balance is now/)).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /adjust anyway/i }),
    ).not.toBeInTheDocument();
    expect(api.requests).toEqual([
      {
        requestId: expect.any(String),
        childId: fredster.id,
        amount: -5,
        reason: "Lost a book",
      },
    ]);
  });

  it("shows server rejections and reuses the request ID for the retry", async () => {
    let attempt = 0;
    const api = fakeApi(adultBoard, (body) => {
      attempt += 1;
      return attempt === 1
        ? jsonResponse(
            {
              title: "Invalid point adjustment data",
              errors: {
                ChildId: ["Choose an active child in this household."],
              },
            },
            { status: 400 },
          )
        : recorded(body, 4);
    });
    const user = userEvent.setup();
    renderApp(addie);

    const form = await openPanel(user);
    await user.type(within(form).getByLabelText("Number of points"), "4");
    await user.type(within(form).getByLabelText("Reason"), "Chores");
    const submit = within(form).getByRole("button", {
      name: "Record adjustment",
    });
    await user.click(submit);

    expect(await within(form).findByRole("alert")).toHaveTextContent(
      "Choose an active child in this household.",
    );
    await user.click(submit);
    await screen.findByText("Added 4 points for Fredster. Balance is now 4.");
    await vi.waitFor(() => expect(submit).toBeEnabled());
    await user.type(within(form).getByLabelText("Number of points"), "2");
    await user.type(within(form).getByLabelText("Reason"), "More chores");
    await user.click(submit);
    await vi.waitFor(() => expect(api.requests).toHaveLength(3));

    const [failed, retried, next] = api.requests;
    expect(retried?.requestId).toBe(failed?.requestId);
    expect(next?.requestId).not.toBe(retried?.requestId);
  });

  it("does not show children the adjustment tools", async () => {
    fakeApi({ ...adultBoard, viewer: fredster, pointsBalance: 4 }, (body) =>
      recorded(body, 0),
    );
    renderApp(fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    expect(
      screen.queryByRole("form", { name: "Adjust points" }),
    ).not.toBeInTheDocument();
    expect(document.querySelector(".point-adjustments")).toBeNull();
  });
});

function recorded(body: AdjustmentRequest, balance: number) {
  return jsonResponse(
    {
      adjustment: {
        id: crypto.randomUUID(),
        childId: body.childId,
        adjustedByMemberId: addie.id,
        amount: body.amount,
        reason: body.reason,
        adjustedAtUtc: "2026-09-21T08:00:00Z",
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

async function openPanel(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole("heading", { name: "Good day, Addie!" });
  const summary = document.querySelector(".point-adjustments > summary");
  if (!summary) {
    throw new Error("The adjust points tool was not rendered.");
  }

  await user.click(summary);
  return screen.getByRole("form", { name: "Adjust points" });
}

function fakeApi(
  board: unknown,
  onAdjust: (body: AdjustmentRequest) => Response,
) {
  const requests: AdjustmentRequest[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      if (
        new URL(request.url).pathname === "/api/point-adjustments" &&
        request.method === "POST"
      ) {
        const body = (await request.clone().json()) as AdjustmentRequest;
        requests.push(body);
        return onAdjust(body);
      }

      return jsonResponse(board);
    }),
  );
  return { requests };
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
