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

const baseBoard = {
  members: [addie, fredster, harrie],
  date: "2026-09-21",
  currentDate: "2026-09-21",
  selectedChildId: null,
  pointsBalance: null,
  pendingApprovalCount: 0,
  pointEarnings: [],
  jobs: [],
};

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
  window.localStorage.clear();
});

describe("Whose Turn Is It?", () => {
  it("shows today's answer on the board when configured", async () => {
    fakeApi(addie, {
      whoseTurn: {
        question: "Who is Pink today?",
        childId: fredster.id,
        childDisplayName: "Fredster",
      },
    });
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });

    const question = await screen.findByText("Who is Pink today?");
    const card = question.closest(".whose-turn-card");
    expect(card).not.toBeNull();
    expect(
      within(card as HTMLElement).getByText("Fredster"),
    ).toBeInTheDocument();
  });

  it("hides the card and shows a setup invitation to adults when unconfigured", async () => {
    fakeApi(addie, { whoseTurn: null });
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });

    expect(
      await screen.findByText(
        "Whose Turn Is It? isn’t set up yet. Configure it in the tools above.",
      ),
    ).toBeInTheDocument();
  });

  it("does not show the setup invitation to children", async () => {
    fakeApi(fredster, { whoseTurn: null });
    renderApp(fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });

    expect(
      screen.queryByText(/Whose Turn Is It\? isn’t set up yet/),
    ).not.toBeInTheDocument();
  });

  it("lets an adult choose participants, order, and first turn, then saves", async () => {
    const api = fakeApi(addie, { whoseTurn: null });
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Set it up"));
    const form = await screen.findByRole("form", {
      name: "Configure whose turn is it",
    });

    await user.click(within(form).getByLabelText("Fredster"));
    await user.click(within(form).getByLabelText("Harrie"));

    const order = within(form).getByRole("list");
    expect(
      within(order)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([
      expect.stringContaining("Fredster"),
      expect.stringContaining("Harrie"),
    ]);
    await user.click(
      within(order).getByRole("button", {
        name: "Move Harrie earlier in the order",
      }),
    );
    expect(
      within(order)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([
      expect.stringContaining("Harrie"),
      expect.stringContaining("Fredster"),
    ]);

    await user.selectOptions(
      within(form).getByLabelText("First turn"),
      "Fredster",
    );
    const effectiveInput = within(form).getByLabelText("Effective from");
    expect(effectiveInput).toHaveValue("2026-09-21");
    await user.type(
      within(form).getByLabelText("Question (optional)"),
      "Who feeds the fish?",
    );
    await user.click(
      within(form).getByRole("button", { name: "Set up rotation" }),
    );

    expect(
      await screen.findByText("Whose Turn Is It? rotation saved."),
    ).toBeInTheDocument();
    expect(api.saveRequests).toHaveLength(1);
    expect(api.saveRequests[0]).toEqual({
      participantChildIds: [harrie.id, fredster.id],
      firstChildId: fredster.id,
      effectiveFrom: "2026-09-21",
      question: "Who feeds the fish?",
    });

    const preview = await screen.findByRole("heading", {
      name: "Upcoming turns",
    });
    expect(preview).toBeInTheDocument();
  });

  it("drops a deactivated participant from the form so a repair can be saved", async () => {
    const gone = "99999999-9999-4999-8999-999999999999";
    const api = fakeApi(
      addie,
      { whoseTurn: null },
      {
        id: "11111111-1111-4111-8111-111111111111",
        effectiveFrom: "2026-09-01",
        question: "Who is Pink today?",
        participantChildIds: [gone, harrie.id],
        firstChildId: gone,
        createdByMemberId: addie.id,
        createdAtUtc: "2026-09-01T08:00:00Z",
      },
    );
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Set it up"));
    const form = await screen.findByRole("form", {
      name: "Configure whose turn is it",
    });

    expect(
      within(within(form).getByRole("list"))
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([expect.stringContaining("Harrie")]);
    expect(within(form).getByLabelText("First turn")).toHaveValue(harrie.id);
    await user.click(
      within(form).getByRole("button", { name: "Save changes" }),
    );

    await screen.findByText("Whose Turn Is It? rotation saved.");
    expect(api.saveRequests[0]).toMatchObject({
      participantChildIds: [harrie.id],
      firstChildId: harrie.id,
    });
  });

  it("disables submit until at least one participant is selected", async () => {
    fakeApi(addie, { whoseTurn: null });
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Set it up"));
    const form = await screen.findByRole("form", {
      name: "Configure whose turn is it",
    });

    expect(
      within(form).getByRole("button", { name: "Set up rotation" }),
    ).toBeDisabled();
  });
});

function fakeApi(
  viewer: ReturnType<typeof member>,
  boardExtras: { whoseTurn: unknown },
  initialCurrent: Record<string, unknown> | null = null,
) {
  const board = { viewer, ...baseBoard, ...boardExtras };
  const state = {
    saveRequests: [] as unknown[],
    current: initialCurrent,
  };
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      const path = new URL(request.url).pathname;
      const method = request.method;
      if (path === "/api/turn-rotation" && method === "GET") {
        return jsonResponse({
          current: state.current,
          hasRevisions: state.current !== null,
          upcomingTurns: state.current
            ? [
                {
                  date: "2026-09-21",
                  childDisplayName: "Fredster",
                  question: (state.current.question as string) ?? "",
                  childId: (state.current.participantChildIds as string[])[0],
                },
              ]
            : [],
        });
      }
      if (path === "/api/turn-rotation" && method === "PUT") {
        const body = await request.clone().json();
        state.saveRequests.push(body);
        state.current = {
          id: crypto.randomUUID(),
          effectiveFrom: body.effectiveFrom,
          question: body.question ?? "Who is Pink today?",
          participantChildIds: body.participantChildIds,
          firstChildId: body.firstChildId,
          createdByMemberId: viewer.id,
          createdAtUtc: "2026-09-21T08:00:00Z",
        };
        return jsonResponse({
          current: state.current,
          hasRevisions: true,
          upcomingTurns: [
            {
              childDisplayName: "Fredster",
              date: body.effectiveFrom,
              question: state.current.question,
              childId: body.firstChildId,
            },
          ],
        });
      }
      return jsonResponse(board);
    }),
  );

  return {
    get saveRequests() {
      return state.saveRequests;
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
