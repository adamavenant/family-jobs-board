import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { acceptSession, clearIdentity } from "../../api/auth";
import type { WhoseTurn } from "../../api/today";
import { routes } from "../../app/routes";

const addie = member("22eb0cc1-058e-4b2e-bb18-d7aaad564a6c", "Addie", true);
const fredster = member(
  "754de05d-b6f6-4626-bbad-79e2079cc5c3",
  "Fredster",
  false,
);
const harrie = member("e22facf5-69ce-45ce-9dad-306eef1852c9", "Harrie", false);
const pinkId = "11111111-1111-4111-8111-111111111111";
const mumId = "22222222-2222-4222-8222-222222222222";
const showerId = "33333333-3333-4333-8333-333333333333";
const turnId = "44444444-4444-4444-8444-444444444444";
const revisionId = "66666666-6666-4666-8666-666666666666";

const baseBoard = {
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

describe("Whose Turn Is It?", () => {
  it("shows one card per rotation on the board", async () => {
    fakeApi(addie, {
      whoseTurns: [
        turn(pinkId, "Who is Pink today?", fredster),
        turn(mumId, "Who sits next to Mum?", harrie),
      ],
    });
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });

    const pink = (await screen.findByText("Who is Pink today?")).closest(
      ".whose-turn-card",
    ) as HTMLElement;
    const mum = (await screen.findByText("Who sits next to Mum?")).closest(
      ".whose-turn-card",
    ) as HTMLElement;
    expect(within(pink).getByText("Fredster")).toBeInTheDocument();
    expect(within(mum).getByText("Harrie")).toBeInTheDocument();
  });

  it("shows a setup invitation to adults only when nothing is configured", async () => {
    fakeApi(addie, { whoseTurns: [] });
    renderApp(addie);
    await screen.findByRole("heading", { name: "Good day, Addie!" });
    expect(
      await screen.findByText(/Whose Turn Is It\? isn’t set up yet/),
    ).toBeInTheDocument();
  });

  it("does not show the setup invitation to children", async () => {
    fakeApi(fredster, { whoseTurns: [] });
    renderApp(fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });

    expect(
      screen.queryByText(/Whose Turn Is It\? isn’t set up yet/),
    ).not.toBeInTheDocument();
  });

  it("lets an adult add a rotation with participants, order, and first turn", async () => {
    const api = fakeApi(addie, { whoseTurns: [] });
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    const form = await screen.findByRole("form", { name: "Add a rotation" });

    await user.click(within(form).getByLabelText("Fredster"));
    await user.click(within(form).getByLabelText("Harrie"));
    const order = within(form).getByRole("list");
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
    expect(within(form).getByLabelText("Effective from")).toHaveValue(
      "2026-09-21",
    );
    await user.type(
      within(form).getByLabelText("Question (optional)"),
      "Who feeds the fish?",
    );
    await user.click(
      within(form).getByRole("button", { name: "Add rotation" }),
    );

    expect(
      await screen.findByText("Whose Turn Is It? rotation saved."),
    ).toBeInTheDocument();
    expect(api.requests).toEqual([
      {
        method: "POST",
        path: "/api/turn-rotations",
        body: {
          participantChildIds: [harrie.id, fredster.id],
          firstChildId: fredster.id,
          effectiveFrom: "2026-09-21",
          question: "Who feeds the fish?",
          advanceMode: "daily",
        },
      },
    ]);
    expect(
      await screen.findByRole("list", { name: "Rotations" }),
    ).toBeInTheDocument();
  });

  it("adds a second rotation alongside an existing one", async () => {
    const api = fakeApi(addie, { whoseTurns: [] }, [
      rotation(pinkId, "Who is Pink today?", [fredster, harrie], fredster),
    ]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    const form = await screen.findByRole("form", { name: "Add a rotation" });
    await user.click(within(form).getByLabelText("Harrie"));
    await user.selectOptions(
      within(form).getByLabelText("First turn"),
      "Harrie",
    );
    await user.type(
      within(form).getByLabelText("Question (optional)"),
      "Who sits next to Mum?",
    );
    await user.click(
      within(form).getByRole("button", { name: "Add rotation" }),
    );

    await screen.findByText("Whose Turn Is It? rotation saved.");
    expect(api.requests[0]).toMatchObject({
      method: "POST",
      path: "/api/turn-rotations",
    });
    const list = await screen.findByRole("list", { name: "Rotations" });
    expect(within(list).getAllByRole("button", { name: /^End / })).toHaveLength(
      2,
    );
    expect(within(list).getByText("Who sits next to Mum?")).toBeInTheDocument();
    expect(within(list).getByText("Who is Pink today?")).toBeInTheDocument();
  });

  it("drops a deactivated participant when editing so the repair can be saved", async () => {
    const gone = "99999999-9999-4999-8999-999999999999";
    const api = fakeApi(addie, { whoseTurns: [] }, [
      {
        ...rotation(pinkId, "Who is Pink today?", [harrie], harrie),
        current: {
          ...rotation(pinkId, "Who is Pink today?", [harrie], harrie).current,
          participantChildIds: [gone, harrie.id],
          firstChildId: gone,
        },
      },
    ]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    const form = await screen.findByRole("form", {
      name: "Configure Who is Pink today?",
    });
    expect(
      within(within(form).getByRole("list"))
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([expect.stringContaining("Harrie")]);
    await user.click(
      within(form).getByRole("button", { name: "Save changes" }),
    );

    await screen.findByText("Whose Turn Is It? rotation saved.");
    expect(api.requests[0]).toMatchObject({
      method: "PUT",
      path: `/api/turn-rotations/${pinkId}`,
      body: { participantChildIds: [harrie.id], firstChildId: harrie.id },
    });
  });

  it("ends a rotation after confirmation", async () => {
    const api = fakeApi(addie, { whoseTurns: [] }, [
      rotation(pinkId, "Who is Pink today?", [fredster], fredster),
    ]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    await user.click(
      await screen.findByRole("button", { name: "End Who is Pink today?" }),
    );
    await user.click(screen.getByRole("button", { name: "Yes, end it" }));

    expect(
      await screen.findByText(
        "Rotation ended. It stops appearing from tomorrow.",
      ),
    ).toBeInTheDocument();
    expect(api.requests).toEqual([
      {
        method: "DELETE",
        path: `/api/turn-rotations/${pinkId}`,
        body: undefined,
      },
    ]);
  });

  it.each([false, true])(
    "reconciles ending a same-name rotation (missing: %s)",
    async (missingOnEnd) => {
      fakeApi(
        addie,
        {
          whoseTurns: [
            turn(pinkId, "Who is Pink today?", fredster),
            turn(mumId, "Who is Pink today?", harrie),
          ],
        },
        [
          rotation(pinkId, "Who is Pink today?", [fredster], fredster),
          rotation(mumId, "Who is Pink today?", [harrie], harrie),
        ],
        undefined,
        undefined,
        missingOnEnd,
      );
      const user = userEvent.setup();
      const view = renderApp(addie);
      await screen.findByRole("heading", { name: "Good day, Addie!" });
      await user.click(screen.getByText("Manage rotations"));
      const list = await screen.findByRole("list", { name: "Rotations" });
      await user.click(
        within(list).getAllByRole("button", {
          name: "End Who is Pink today?",
        })[0]!,
      );
      await user.click(screen.getByRole("button", { name: "Yes, end it" }));
      if (missingOnEnd) {
        expect(await screen.findByRole("alert")).toHaveTextContent(
          "That rotation is no longer available",
        );
        expect(
          within(list).queryByText("Today: Fredster"),
        ).not.toBeInTheDocument();
        expect(document.querySelectorAll(".whose-turn-card")).toHaveLength(1);
      } else {
        await screen.findByText(
          "Ends tomorrow. Today’s answer stays as it is.",
        );
        expect(within(list).getByText("Today: Fredster")).toBeInTheDocument();
      }
      expect(
        within(list).getAllByRole("button", { name: "End Who is Pink today?" }),
      ).toHaveLength(1);
      expect(within(list).getByText("Today: Harrie")).toBeInTheDocument();
      view.unmount();
      renderApp(addie);
      await screen.findByRole("heading", { name: "Good day, Addie!" });
      await user.click(screen.getByText("Manage rotations"));
      const reloaded = await screen.findByRole("list", { name: "Rotations" });
      expect(
        within(reloaded).getAllByRole("button", {
          name: "End Who is Pink today?",
        }),
      ).toHaveLength(1);
    },
  );

  it("disables submit until at least one participant is selected", async () => {
    fakeApi(addie, { whoseTurns: [] });
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    const form = await screen.findByRole("form", { name: "Add a rotation" });

    expect(
      within(form).getByRole("button", { name: "Add rotation" }),
    ).toBeDisabled();
  });

  it("lets the child whose turn it is tick it off and shows it done", async () => {
    const api = fakeApi(
      fredster,
      { whoseTurns: [ticked(fredster, { canTakeTurn: true })] },
      [],
      {
        whoseTurns: [
          ticked(fredster, {
            turnTaken: true,
            undoableTurn: undoable(fredster),
          }),
        ],
      },
    );
    const user = userEvent.setup();
    renderApp(fredster);

    const tick = await screen.findByRole("checkbox", {
      name: "Fredster has had their turn",
    });
    expect(tick).not.toBeChecked();
    await user.click(tick);

    expect(
      await screen.findByText("had their turn", {
        exact: false,
        selector: ".whose-turn-card__taken",
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("checkbox", { name: "Fredster has had their turn" }),
    ).toBeChecked();
    expect(api.requests).toEqual([
      {
        method: "POST",
        path: `/api/turn-rotations/${showerId}/turns`,
        body: {
          requestId: expect.any(String),
          revisionId,
          turnNumber: 0,
          childId: fredster.id,
        },
      },
    ]);
  });

  it("unticks a turn that is done for today with the same checkbox", async () => {
    const api = fakeApi(
      addie,
      {
        whoseTurns: [
          ticked(fredster, {
            turnTaken: true,
            undoableTurn: undoable(fredster),
          }),
        ],
      },
      [],
      { whoseTurns: [ticked(fredster, { canTakeTurn: true })] },
    );
    const user = userEvent.setup();
    renderApp(addie);

    await user.click(
      await screen.findByRole("checkbox", {
        name: "Fredster has had their turn",
      }),
    );

    await waitFor(() =>
      expect(
        screen.getByRole("checkbox", { name: "Fredster has had their turn" }),
      ).not.toBeChecked(),
    );
    expect(api.requests).toEqual([
      {
        method: "DELETE",
        path: `/api/turn-rotations/${showerId}/turns/${turnId}`,
        body: undefined,
      },
    ]);
  });

  it("offers an undo once a two-child turn has passed to the other child", async () => {
    const api = fakeApi(
      fredster,
      { whoseTurns: [ticked(harrie, { undoableTurn: undoable(fredster) })] },
      [],
      { whoseTurns: [ticked(fredster, { canTakeTurn: true })] },
    );
    const user = userEvent.setup();
    renderApp(fredster);

    const card = (
      await screen.findByText("Who is first in the shower?")
    ).closest(".whose-turn-card") as HTMLElement;
    expect(within(card).getByText("Harrie")).toBeInTheDocument();
    expect(within(card).queryByRole("checkbox")).not.toBeInTheDocument();
    expect(
      within(card).getByText("Fredster just had a turn."),
    ).toBeInTheDocument();
    await user.click(
      within(card).getByRole("button", { name: "Undo Fredster’s turn" }),
    );

    expect(
      await screen.findByRole("checkbox", {
        name: "Fredster has had their turn",
      }),
    ).toBeInTheDocument();
    expect(api.requests).toEqual([
      {
        method: "DELETE",
        path: `/api/turn-rotations/${showerId}/turns/${turnId}`,
        body: undefined,
      },
    ]);
  });

  it("shows no tick controls to other children, on other days, or for daily rotations", async () => {
    fakeApi(harrie, {
      whoseTurns: [
        ticked(fredster, { turnTaken: true }),
        turn(pinkId, "Who is Pink today?", harrie),
      ],
    });
    const { unmount } = renderApp(harrie);

    expect(
      await screen.findByText("had their turn", {
        exact: false,
        selector: ".whose-turn-card__taken",
      }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Undo/ })).toBeNull();
    unmount();

    fakeApi(fredster, {
      date: "2026-09-22",
      whoseTurns: [ticked(fredster, { canTakeTurn: true })],
    });
    renderApp(fredster);
    expect(
      await screen.findByText("Who is first in the shower? (Tuesday)"),
    ).toBeInTheDocument();
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("explains a refused tick and reloads the board", async () => {
    fakeApi(
      fredster,
      { whoseTurns: [ticked(fredster, { canTakeTurn: true })] },
      [],
      { whoseTurns: [ticked(harrie, {})] },
      {
        status: 409,
        detail:
          "It's Harrie's turn now. Refresh the board to see the latest turn.",
      },
    );
    const user = userEvent.setup();
    renderApp(fredster);

    await user.click(
      await screen.findByRole("checkbox", {
        name: "Fredster has had their turn",
      }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "It's Harrie's turn now.",
    );
    const card = screen
      .getByText("Who is first in the shower?")
      .closest(".whose-turn-card") as HTMLElement;
    expect(within(card).getByText("Harrie")).toBeInTheDocument();
  });

  it("adds a rotation that moves on when a turn is ticked off", async () => {
    const api = fakeApi(addie, { whoseTurns: [] });
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    const form = await screen.findByRole("form", { name: "Add a rotation" });
    expect(within(form).getByLabelText("Every day")).toBeChecked();
    await user.click(within(form).getByLabelText("Fredster"));
    await user.click(within(form).getByLabelText("Harrie"));
    await user.selectOptions(
      within(form).getByLabelText("First turn"),
      "Fredster",
    );
    await user.click(within(form).getByLabelText("When a turn is ticked off"));
    expect(
      within(form).getByText(/the turn passes straight to the other child/),
    ).toBeInTheDocument();
    await user.click(
      within(form).getByRole("button", { name: "Add rotation" }),
    );

    await screen.findByText("Whose Turn Is It? rotation saved.");
    expect(api.requests[0]).toMatchObject({
      method: "POST",
      path: "/api/turn-rotations",
      body: { advanceMode: "whenTicked" },
    });
  });

  it("previews who is up next and carries on from them when editing a ticked rotation", async () => {
    const api = fakeApi(addie, { whoseTurns: [] }, [
      showerRotation([harrie, fredster]),
    ]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    expect(
      await screen.findByText("Moves on when a turn is ticked off"),
    ).toBeInTheDocument();
    const form = await screen.findByRole("form", {
      name: "Configure Who is first in the shower?",
    });
    expect(
      within(form).getByLabelText("When a turn is ticked off"),
    ).toBeChecked();
    expect(within(form).getByLabelText("First turn")).toHaveValue(harrie.id);
    const upNext = screen.getByRole("region", { name: "Up next" });
    expect(
      within(upNext)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual(["Harrie", "Fredster"]);
    expect(screen.queryByText("Upcoming turns")).not.toBeInTheDocument();

    await user.click(
      within(form).getByRole("button", { name: "Save changes" }),
    );
    await screen.findByText("Whose Turn Is It? rotation saved.");
    expect(api.requests[0]).toMatchObject({
      method: "PUT",
      body: { firstChildId: harrie.id, advanceMode: "whenTicked" },
    });
  });

  it("carries an untouched first turn on from whoever is next after a tick on the board", async () => {
    fakeApi(
      addie,
      { whoseTurns: [ticked(fredster, { canTakeTurn: true })] },
      [showerRotation([fredster, harrie])],
      {
        whoseTurns: [
          ticked(harrie, {
            canTakeTurn: true,
            turnNumber: 1,
            undoableTurn: undoable(fredster),
          }),
        ],
        rotations: [showerRotation([harrie, fredster])],
      },
    );
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    const form = await screen.findByRole("form", {
      name: "Configure Who is first in the shower?",
    });
    expect(within(form).getByLabelText("First turn")).toHaveValue(fredster.id);

    await user.click(
      screen.getByRole("checkbox", { name: "Fredster has had their turn" }),
    );
    await screen.findByText("Fredster just had a turn.");
    await user.click(screen.getByText("Edit Who is first in the shower?"));

    await waitFor(() =>
      expect(within(form).getByLabelText("First turn")).toHaveValue(harrie.id),
    );
  });

  it("keeps a first turn the adult picked when the board moves on", async () => {
    fakeApi(
      addie,
      { whoseTurns: [ticked(fredster, { canTakeTurn: true })] },
      [showerRotation([fredster, harrie])],
      [
        {
          whoseTurns: [ticked(harrie, { canTakeTurn: true, turnNumber: 1 })],
          rotations: [showerRotation([harrie, fredster])],
        },
        {
          whoseTurns: [ticked(fredster, { canTakeTurn: true, turnNumber: 2 })],
          rotations: [showerRotation([fredster, harrie])],
        },
      ],
    );
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage rotations"));
    const form = await screen.findByRole("form", {
      name: "Configure Who is first in the shower?",
    });
    await user.click(screen.getByText("Edit Who is first in the shower?"));
    await user.selectOptions(
      within(form).getByLabelText("First turn"),
      "Harrie",
    );

    await user.click(
      screen.getByRole("checkbox", { name: "Fredster has had their turn" }),
    );
    await user.click(
      await screen.findByRole("checkbox", {
        name: "Harrie has had their turn",
      }),
    );
    await screen.findByRole("checkbox", {
      name: "Fredster has had their turn",
    });

    await waitFor(() =>
      expect(
        within(screen.getByRole("region", { name: "Up next" }))
          .getAllByRole("listitem")
          .map((item) => item.textContent),
      ).toEqual(["Fredster", "Harrie"]),
    );
    expect(within(form).getByLabelText("First turn")).toHaveValue(harrie.id);
  });
});

function turn(
  rotationId: string,
  question: string,
  child: ReturnType<typeof member>,
  overrides: Partial<WhoseTurn> = {},
): WhoseTurn {
  return {
    rotationId,
    question,
    childId: child.id,
    childDisplayName: child.displayName,
    revisionId,
    turnNumber: 0,
    advanceMode: "daily",
    turnTaken: false,
    canTakeTurn: false,
    undoableTurn: null,
    ...overrides,
  };
}

function ticked(
  child: ReturnType<typeof member>,
  overrides: Partial<WhoseTurn>,
): WhoseTurn {
  return turn(showerId, "Who is first in the shower?", child, {
    advanceMode: "whenTicked",
    ...overrides,
  });
}

function showerRotation(upNext: ReturnType<typeof member>[]) {
  const shower = rotation(
    showerId,
    "Who is first in the shower?",
    [fredster, harrie],
    fredster,
  );
  return {
    ...shower,
    current: { ...shower.current, advanceMode: "whenTicked" },
    upNext: upNext.map((child) => ({
      childId: child.id,
      childDisplayName: child.displayName,
    })),
  };
}

function undoable(child: ReturnType<typeof member>) {
  return { turnId, childId: child.id, childDisplayName: child.displayName };
}

function rotation(
  rotationId: string,
  question: string,
  participants: ReturnType<typeof member>[],
  first: ReturnType<typeof member>,
) {
  return {
    rotationId,
    current: {
      id: crypto.randomUUID(),
      effectiveFrom: "2026-09-01",
      question,
      participantChildIds: participants.map((child) => child.id),
      firstChildId: first.id,
      advanceMode: "daily",
      createdByMemberId: addie.id,
      createdAtUtc: "2026-09-01T08:00:00Z",
    },
    upcomingTurns: [
      {
        date: "2026-09-21",
        question,
        childId: first.id,
        childDisplayName: first.displayName,
      },
      {
        date: "2026-09-22",
        question,
        childId: first.id,
        childDisplayName: first.displayName,
      },
    ],
    upNext: [] as { childId: string; childDisplayName: string }[],
  };
}

interface AfterTick {
  whoseTurns: unknown[];
  rotations?: ReturnType<typeof rotation>[];
}

interface RecordedRequest {
  method: string;
  path: string;
  body: unknown;
}

function fakeApi(
  viewer: ReturnType<typeof member>,
  boardExtras: { whoseTurns: unknown[]; date?: string },
  initialRotations: ReturnType<typeof rotation>[] = [],
  boardAfterTick?: AfterTick | AfterTick[],
  tickProblem?: { status: number; detail: string },
  missingOnEnd = false,
) {
  const afterTicks = Array.isArray(boardAfterTick)
    ? [...boardAfterTick]
    : boardAfterTick
      ? [boardAfterTick]
      : [];
  let board = { viewer, ...baseBoard, ...boardExtras };
  const state = {
    rotations: [...initialRotations],
    requests: [] as RecordedRequest[],
  };
  const overview = () => ({ rotations: state.rotations });
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      const path = new URL(request.url).pathname;
      const method = request.method;
      if (!path.startsWith("/api/turn-rotations")) {
        return jsonResponse(board);
      }
      if (path.includes("/turns")) {
        const body =
          method === "DELETE" ? undefined : await request.clone().json();
        state.requests.push({ method, path, body });
        const after = Array.isArray(boardAfterTick)
          ? afterTicks.shift()
          : afterTicks[0];
        if (after) {
          board = { ...board, whoseTurns: after.whoseTurns };
          if (after.rotations) {
            state.rotations = after.rotations;
          }
        }
        if (tickProblem) {
          return jsonResponse(
            { title: "Turn cannot be changed", detail: tickProblem.detail },
            { status: tickProblem.status },
          );
        }
        return jsonResponse({
          id: turnId,
          requestId: body?.requestId ?? crypto.randomUUID(),
          rotationId: showerId,
          revisionId,
          turnNumber: body?.turnNumber ?? 0,
          date: "2026-09-21",
          childId: fredster.id,
          childDisplayName: "Fredster",
          recordedByMemberId: viewer.id,
          recordedAtUtc: "2026-09-21T08:00:00Z",
          undoneByMemberId: method === "DELETE" ? viewer.id : null,
          undoneAtUtc: method === "DELETE" ? "2026-09-21T08:05:00Z" : null,
        });
      }
      if (method === "GET") {
        return jsonResponse(overview());
      }
      const body =
        method === "DELETE" ? undefined : await request.clone().json();
      state.requests.push({ method, path, body });
      const id = path.split("/")[3] ?? "";
      if (method === "DELETE") {
        if (missingOnEnd) {
          board.whoseTurns = board.whoseTurns.filter(
            (entry) =>
              typeof entry !== "object" ||
              entry === null ||
              !("rotationId" in entry) ||
              entry.rotationId !== id,
          );
          state.rotations = state.rotations.filter((r) => r.rotationId !== id);
          return jsonResponse(
            { detail: `Whose-turn rotation ${id} was not found.` },
            { status: 404 },
          );
        }
        state.rotations = state.rotations.map((r) =>
          r.rotationId === id
            ? { ...r, upcomingTurns: r.upcomingTurns.slice(0, 1) }
            : r,
        );
        return jsonResponse(overview());
      }
      const created = rotation(
        id || crypto.randomUUID(),
        body.question ?? "Who is Pink today?",
        [fredster, harrie].filter((c) =>
          body.participantChildIds.includes(c.id),
        ),
        body.firstChildId === harrie.id ? harrie : fredster,
      );
      state.rotations = id
        ? state.rotations.map((r) => (r.rotationId === id ? created : r))
        : [...state.rotations, created];
      return jsonResponse(overview());
    }),
  );

  return {
    get requests() {
      return state.requests;
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
