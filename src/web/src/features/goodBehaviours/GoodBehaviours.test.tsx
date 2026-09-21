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

const brave = {
  id: "c7e94b21-3f8a-4d6b-8e5c-9a0b1d3f4c33",
  name: "Being Brave",
  description: "Tried something hard.",
  points: 10,
};
const helpful = {
  id: "8c2a7c6e-1d4f-4a9e-b7a3-4e1d9c2b7a22",
  name: "Being Helpful",
  description: "Helped without being asked.",
  points: 5,
};

const adultBoard = {
  viewer: addie,
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

describe("Good behaviours", () => {
  it("lets an adult log a behaviour with an edited point value", async () => {
    const api = fakeApi(adultBoard, [brave, helpful]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Log or manage good behaviours"));
    const form = await screen.findByRole("form", {
      name: "Log a good behaviour",
    });
    expect(within(form).getByLabelText("Points to award")).toHaveValue(10);

    await user.click(within(form).getByLabelText("Fredster"));
    await user.click(within(form).getByLabelText("Harrie"));
    await user.selectOptions(
      within(form).getByLabelText("Good behaviour"),
      "Being Helpful",
    );
    expect(within(form).getByLabelText("Points to award")).toHaveValue(5);
    await user.clear(within(form).getByLabelText("Points to award"));
    await user.type(within(form).getByLabelText("Points to award"), "8");
    await user.click(
      within(form).getByRole("button", { name: "Log behaviour" }),
    );

    expect(
      await screen.findByText("Logged Being Helpful for Harrie: +8 points."),
    ).toHaveAttribute("role", "status");
    expect(api.logRequests).toHaveLength(1);
    expect(api.logRequests[0]).toEqual({
      requestId: expect.any(String),
      typeId: helpful.id,
      childIds: [harrie.id],
      points: 8,
    });
  });

  it("logs one behaviour for both children with the same picker as jobs", async () => {
    const api = fakeApi(adultBoard, [brave]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Log or manage good behaviours"));
    const form = await screen.findByRole("form", {
      name: "Log a good behaviour",
    });
    expect(
      within(form).getByRole("group", { name: "Log for" }),
    ).toBeInTheDocument();
    expect(within(form).getByLabelText("Fredster")).toBeChecked();
    expect(within(form).getByLabelText("Harrie")).not.toBeChecked();

    await user.click(within(form).getByLabelText("Harrie"));
    await user.click(
      within(form).getByRole("button", { name: "Log behaviour" }),
    );

    expect(
      await screen.findByText(
        "Logged Being Brave for Fredster and Harrie: +10 points each.",
      ),
    ).toBeInTheDocument();
    expect(api.logRequests).toHaveLength(1);
    expect(api.logRequests[0]?.childIds).toEqual([fredster.id, harrie.id]);
  });

  it("asks for a child and sends nothing when none is selected", async () => {
    const api = fakeApi(adultBoard, [brave]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Log or manage good behaviours"));
    const form = await screen.findByRole("form", {
      name: "Log a good behaviour",
    });
    await user.click(within(form).getByLabelText("Fredster"));
    await user.click(
      within(form).getByRole("button", { name: "Log behaviour" }),
    );

    expect(
      await within(form).findByText("Choose one or more children."),
    ).toHaveAttribute("role", "alert");
    expect(api.logRequests).toHaveLength(0);
  });

  it("reuses the request ID after a failure and uses a new one after success", async () => {
    const api = fakeApi(adultBoard, [brave]);
    api.failNextLog = true;
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Log or manage good behaviours"));
    const form = await screen.findByRole("form", {
      name: "Log a good behaviour",
    });
    const submit = within(form).getByRole("button", { name: "Log behaviour" });

    await user.click(submit);
    expect(await within(form).findByRole("alert")).toBeInTheDocument();
    await user.click(submit);
    await screen.findByText("Logged Being Brave for Fredster: +10 points.");
    await vi.waitFor(() => expect(submit).toBeEnabled());
    await user.click(submit);
    await vi.waitFor(() => expect(api.logRequests).toHaveLength(3));

    const [failed, retried, next] = api.logRequests;
    expect(retried?.requestId).toBe(failed?.requestId);
    expect(next?.requestId).not.toBe(retried?.requestId);
  });

  it("creates, edits and deletes behaviour types", async () => {
    const api = fakeApi(adultBoard, [brave]);
    const user = userEvent.setup();
    renderApp(addie);

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Log or manage good behaviours"));
    const create = await screen.findByRole("form", {
      name: "Add a good behaviour",
    });
    await user.type(within(create).getByLabelText("Name"), "Sharing");
    await user.clear(within(create).getByLabelText("Usual points"));
    await user.type(within(create).getByLabelText("Usual points"), "3");
    await user.click(
      within(create).getByRole("button", { name: "Add good behaviour" }),
    );

    expect(await screen.findByText("Added Sharing.")).toBeInTheDocument();
    const list = screen.getByRole("list", { name: "Good behaviours" });
    expect(within(list).getByText("Sharing")).toBeInTheDocument();
    expect(api.types.map((type) => type.name)).toEqual([
      "Being Brave",
      "Sharing",
    ]);

    await user.click(screen.getByText("Edit Sharing"));
    const sharingId = api.types[1]?.id ?? "";
    const editName = document.getElementById(`edit-${sharingId}-name`);
    expect(editName).not.toBeNull();
    await user.clear(editName as HTMLElement);
    await user.type(editName as HTMLElement, "Sharing toys");
    await user.click(screen.getByRole("button", { name: "Save Sharing" }));
    expect(await screen.findByText("Saved Sharing toys.")).toBeInTheDocument();

    await user.click(
      screen.getByRole("button", { name: "Delete Sharing toys" }),
    );
    expect(
      screen.getByText(/Points already awarded stay exactly as they are/),
    ).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Yes, delete" }));
    expect(
      await screen.findByText(
        "Good behaviour deleted. Past points are unchanged.",
      ),
    ).toBeInTheDocument();
    expect(api.types.map((type) => type.name)).toEqual(["Being Brave"]);
  });

  it("shows children the available behaviours read-only, with logging history", async () => {
    fakeApi(
      {
        ...adultBoard,
        viewer: fredster,
        pointsBalance: 15,
        pointEarnings: [
          {
            id: "0a6f4a52-5b1e-4f0e-9d3a-6f1c2f7d8e01",
            source: "goodBehaviour",
            name: "Being Brave",
            jobId: null,
            points: 12,
            awardedAtUtc: "2026-09-21T08:00:00Z",
            loggedByDisplayName: "Addie",
          },
          {
            id: "3b8d1f77-2c4a-4b6e-8f10-9e2d5a7c1b02",
            source: "job",
            name: "Feed the dog",
            jobId: "cf41c6dc-dc4c-4bda-9c23-6b3671a93b81",
            points: 3,
            awardedAtUtc: "2026-09-20T08:00:00Z",
            loggedByDisplayName: null,
          },
        ],
      },
      [brave, helpful],
    );
    const user = userEvent.setup();
    renderApp(fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    const history = screen
      .getByRole("heading", { name: "How Fredster earned them" })
      .closest("section") as HTMLElement;
    expect(within(history).getByText("+12")).toBeInTheDocument();
    expect(within(history).getByText("Being Brave")).toBeInTheDocument();
    expect(
      within(history).getByText("Good behaviour · logged by Addie"),
    ).toBeInTheDocument();
    expect(within(history).getByText("Feed the dog")).toBeInTheDocument();

    await user.click(screen.getByText("Ways to earn points"));
    const list = await screen.findByRole("list", { name: "Good behaviours" });
    expect(within(list).getByText("Being Brave")).toBeInTheDocument();
    expect(within(list).getByText("usually 10 points")).toBeInTheDocument();
    expect(within(list).getByText("usually 5 points")).toBeInTheDocument();
    expect(
      screen.queryByRole("form", { name: "Log a good behaviour" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("form", { name: "Add a good behaviour" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/^Delete /)).not.toBeInTheDocument();
  });
});

interface LoggedRequest {
  requestId: string;
  typeId: string;
  childIds: string[];
  points: number | null;
}

interface StoredType {
  id: string;
  name: string;
  description: string;
  points: number;
}

function fakeApi(board: unknown, initialTypes: StoredType[]) {
  const state = {
    types: [...initialTypes],
    logRequests: [] as LoggedRequest[],
    failNextLog: false,
  };
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      const path = new URL(request.url).pathname;
      const method = request.method;
      if (path === "/api/good-behaviour-types" && method === "GET") {
        return jsonResponse({ types: state.types });
      }
      if (path === "/api/good-behaviour-types" && method === "POST") {
        const body = (await request.clone().json()) as Omit<StoredType, "id">;
        const created = { id: crypto.randomUUID(), ...body };
        state.types.push(created);
        return jsonResponse(created, { status: 201 });
      }
      if (path.startsWith("/api/good-behaviour-types/") && method === "PUT") {
        const id = path.split("/").pop();
        const body = (await request.clone().json()) as Omit<StoredType, "id">;
        const index = state.types.findIndex((type) => type.id === id);
        state.types[index] = { id: id ?? "", ...body };
        return jsonResponse(state.types[index]);
      }
      if (
        path.startsWith("/api/good-behaviour-types/") &&
        method === "DELETE"
      ) {
        const id = path.split("/").pop();
        state.types = state.types.filter((type) => type.id !== id);
        return new Response(null, { status: 204 });
      }
      if (path === "/api/good-behaviours" && method === "POST") {
        const body = (await request.clone().json()) as LoggedRequest;
        state.logRequests.push(body);
        if (state.failNextLog) {
          state.failNextLog = false;
          return jsonResponse(
            { title: "Server error", detail: "The log could not be saved." },
            { status: 500 },
          );
        }
        const type = state.types.find((item) => item.id === body.typeId);
        const points = body.points ?? type?.points ?? 0;
        return jsonResponse(
          {
            awards: body.childIds.map((childId) => ({
              behaviour: {
                id: crypto.randomUUID(),
                typeId: body.typeId,
                typeName: type?.name ?? "",
                typeDescription: type?.description ?? "",
                childId,
                loggedByMemberId: addie.id,
                points,
                loggedAtUtc: "2026-09-21T08:00:00Z",
              },
              pointsBalance: points,
            })),
          },
          { status: 201 },
        );
      }
      return jsonResponse(board);
    }),
  );

  return {
    get types() {
      return state.types;
    },
    get logRequests() {
      return state.logRequests;
    },
    set failNextLog(value: boolean) {
      state.failNextLog = value;
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
