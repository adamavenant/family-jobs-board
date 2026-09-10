import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { acceptSession, clearIdentity } from "../../api/auth";
import { routes } from "../../app/routes";

const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";
const fredsterId = "754de05d-b6f6-4626-bbad-79e2079cc5c3";

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
  window.localStorage.clear();
});

describe("Identity flow", () => {
  it("validates matching adult PINs before bootstrap", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(problemResponse(401))
      .mockResolvedValueOnce(jsonResponse({ state: "createFirstAdult" }));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Create the first grown-up" });
    await user.type(screen.getByLabelText("First name"), "Addie");
    await user.type(screen.getByLabelText("Surname"), "Avenant");
    await user.type(screen.getByLabelText("6-digit PIN"), "012345");
    await user.type(screen.getByLabelText("Confirm PIN"), "012346");
    await user.click(
      screen.getByRole("button", { name: "Set up and continue" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "matching six-digit PIN",
    );
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("preserves a leading zero while bootstrapping", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(problemResponse(401))
      .mockResolvedValueOnce(jsonResponse({ state: "createFirstAdult" }))
      .mockResolvedValueOnce(
        jsonResponse(authResponse(addieId, "Addie", "adult"), { status: 201 }),
      )
      .mockResolvedValueOnce(jsonResponse(board("Addie", true)));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Create the first grown-up" });
    await user.type(screen.getByLabelText("First name"), "Addie");
    await user.type(screen.getByLabelText("Surname"), "Avenant");
    await user.type(screen.getByLabelText("6-digit PIN"), "012345");
    await user.type(screen.getByLabelText("Confirm PIN"), "012345");
    await user.click(
      screen.getByRole("button", { name: "Set up and continue" }),
    );

    expect(
      await screen.findByRole("heading", { name: "Good day, Addie!" }),
    ).toBeInTheDocument();
    const bootstrapCall = fetchMock.mock.calls[2];
    expect(
      JSON.parse(String((bootstrapCall?.[1] as RequestInit).body)),
    ).toMatchObject({ pin: "012345" });
    expect(window.localStorage).toHaveLength(0);
  });

  it("shows a generic sign-in error", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(problemResponse(401))
        .mockResolvedValueOnce(
          jsonResponse({
            state: "signIn",
            members: [
              {
                id: addieId,
                displayName: "Addie",
                role: "adult",
                requiresSurname: false,
              },
            ],
          }),
        )
        .mockResolvedValueOnce(
          problemResponse(
            401,
            "The member or PIN was not accepted.",
            "invalid_credentials",
          ),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Who’s using the board?" });
    await user.type(screen.getByLabelText("6-digit PIN"), "111111");
    await user.click(screen.getByRole("button", { name: "Open my board" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The member or PIN was not accepted.",
    );
    expect(screen.getByLabelText("Profile")).toHaveFocus();
  });

  it("returns an expired session to the profile chooser", async () => {
    acceptSession({
      ...authResponse(addieId, "Addie", "adult"),
      accessTokenExpiresAtUtc: "2000-01-01T00:00:00Z",
    });
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(problemResponse(401))
        .mockResolvedValueOnce(
          jsonResponse({
            state: "signIn",
            members: [
              {
                id: addieId,
                displayName: "Addie",
                role: "adult",
                requiresSurname: false,
              },
            ],
          }),
        ),
    );

    renderApp();

    expect(
      await screen.findByRole("heading", { name: "Who’s using the board?" }),
    ).toBeInTheDocument();
  });

  it("hands off in memory, sets a child PIN, and signs the child in", async () => {
    acceptSession(authResponse(addieId, "Addie", "adult"));
    const adultBoard = board("Addie", true);
    adultBoard.members.push({
      id: fredsterId,
      firstName: "Fredster",
      nickname: null,
      displayName: "Fredster",
      isAdult: false,
    });
    let childSignedIn = false;
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const request = input instanceof Request ? input : undefined;
        const path = new URL(
          request?.url ?? String(input),
          window.location.origin,
        ).pathname;
        if (path === "/api/users" && request?.method === "GET") {
          return jsonResponse([
            {
              id: addieId,
              firstName: "Addie",
              surname: "Avenant",
              nickname: null,
              displayName: "Addie",
              role: "adult",
              isCredentialReady: true,
              isActive: true,
            },
            {
              id: fredsterId,
              firstName: "Fredster",
              surname: null,
              nickname: null,
              displayName: "Fredster",
              role: "child",
              isCredentialReady: false,
              isActive: true,
            },
          ]);
        }
        if (path === `/api/users/${fredsterId}/pin-setup`) {
          return jsonResponse({
            setupToken: "one-time-token",
            expiresAtUtc: "2099-01-01T00:05:00Z",
            targetDisplayName: "Fredster",
            targetRole: "child",
          });
        }
        if (path === "/api/auth/setup-pin") {
          childSignedIn = true;
          return jsonResponse(authResponse(fredsterId, "Fredster", "child"));
        }
        return jsonResponse(
          childSignedIn ? board("Fredster", false) : adultBoard,
        );
      }),
    );
    const user = userEvent.setup();
    const router = renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage family"));
    await screen.findByText("PIN not set");
    const setupButton = screen.getByRole("button", {
      name: "Set up PIN now",
    });
    const handoffForm = setupButton.closest("form");
    expect(handoffForm).not.toBeNull();
    await user.type(
      within(handoffForm as HTMLFormElement).getByLabelText("Surname"),
      "Avenant",
    );
    await user.click(setupButton);

    expect(
      await screen.findByRole("heading", { name: "Over to Fredster" }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/");
    expect(router.state.location.search).toBe("");
    expect(window.localStorage).toHaveLength(0);

    await user.type(screen.getByLabelText("4-digit PIN"), "0123");
    await user.type(screen.getByLabelText("Confirm PIN"), "0123");
    await user.click(
      screen.getByRole("button", { name: "Save PIN and open board" }),
    );

    expect(
      await screen.findByRole("heading", { name: "Good day, Fredster!" }),
    ).toBeInTheDocument();
  });
});

function renderApp() {
  const router = createMemoryRouter(routes, { initialEntries: ["/"] });
  render(<RouterProvider router={router} />);
  return router;
}

function authResponse(
  id: string,
  displayName: string,
  role: "adult" | "child",
) {
  return {
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id, displayName, role },
  };
}

function board(displayName: string, isAdult: boolean) {
  return {
    viewer: {
      id: isAdult ? addieId : fredsterId,
      firstName: displayName,
      nickname: null,
      displayName,
      isAdult,
    },
    members: [] as Array<{
      id: string;
      firstName: string;
      nickname: null;
      displayName: string;
      isAdult: boolean;
    }>,
    date: "2026-09-06",
    jobs: [],
    pointsBalance: isAdult ? null : 0,
    pointEarnings: [],
    pendingApprovalCount: 0,
  };
}

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
}

function problemResponse(
  status: number,
  detail = "Expired",
  code = "session_expired",
) {
  return jsonResponse({ detail, code }, { status });
}
