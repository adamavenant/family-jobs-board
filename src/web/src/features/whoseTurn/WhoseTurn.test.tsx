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
const pinkId = "11111111-1111-4111-8111-111111111111";
const mumId = "22222222-2222-4222-8222-222222222222";

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
});

function turn(
  rotationId: string,
  question: string,
  child: ReturnType<typeof member>,
) {
  return {
    rotationId,
    question,
    childId: child.id,
    childDisplayName: child.displayName,
  };
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
    ],
  };
}

interface RecordedRequest {
  method: string;
  path: string;
  body: unknown;
}

function fakeApi(
  viewer: ReturnType<typeof member>,
  boardExtras: { whoseTurns: unknown[] },
  initialRotations: ReturnType<typeof rotation>[] = [],
) {
  const board = { viewer, ...baseBoard, ...boardExtras };
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
      if (method === "GET") {
        return jsonResponse(overview());
      }
      const body =
        method === "DELETE" ? undefined : await request.clone().json();
      state.requests.push({ method, path, body });
      const id = path.split("/")[3] ?? "";
      if (method === "DELETE") {
        state.rotations = state.rotations.filter((r) => r.rotationId !== id);
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
