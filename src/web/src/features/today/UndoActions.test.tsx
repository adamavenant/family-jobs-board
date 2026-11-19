import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { acceptSession, clearIdentity } from "../../api/auth";
import { routes } from "../../app/routes";
import { recordChanges, saveQueuedAction } from "../../test/undo";
import { expectAnnounced } from "../../test/undo";

const addie = member("22eb0cc1-058e-4b2e-bb18-d7aaad564a6c", "Addie", true);
const fredster = member(
  "754de05d-b6f6-4626-bbad-79e2079cc5c3",
  "Fredster",
  false,
);
const pendingJob = {
  id: "ea64b5d3-ab18-4c75-bc33-eb3cbf7524f6",
  childId: fredster.id,
  childDisplayName: fredster.displayName,
  name: "Clear the table",
  description: "Take dishes to the kitchen after dinner.",
  points: 5,
  scheduledDate: "2026-08-29",
  agendaPeriod: "evening",
  scheduledTime: null,
  recurringJobSeriesId: null,
  recurrenceFrequency: null,
  status: "pendingApproval",
  completedAtUtc: "2026-08-29T09:00:00Z",
  approvedAtUtc: null,
  latestRejection: null,
};

interface Sent {
  method: string;
  path: string;
  body?: unknown;
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  clearIdentity();
});

/** A small fake API: the board, approvals, sign-out, and household settings. */
function fakeApi({
  viewer = addie,
  undoGracePeriodSeconds = 10,
  refuseApprovals = false,
}: {
  viewer?: typeof addie;
  undoGracePeriodSeconds?: number;
  refuseApprovals?: boolean;
} = {}) {
  const state = {
    undoGracePeriodSeconds,
    approved: false,
    sent: [] as Sent[],
  };
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = input instanceof Request ? input : null;
      const url = new URL(
        request?.url ?? String(input),
        "http://dashboard.home.arpa",
      );
      const method = (request?.method ?? init?.method ?? "GET").toUpperCase();
      if (method !== "GET") {
        state.sent.push({
          method,
          path: url.pathname,
          body: request ? await request.clone().text() : init?.body,
        });
      }
      if (url.pathname === "/api/today") {
        return jsonResponse({
          viewer,
          members: [addie, fredster],
          date: "2026-08-29",
          currentDate: "2026-08-29",
          selectedChildId: null,
          pointsBalance: viewer.isAdult ? null : 0,
          pendingApprovalCount: state.approved ? 0 : 1,
          jobs: [
            state.approved ? { ...pendingJob, status: "approved" } : pendingJob,
          ],
          whoseTurns: [],
          undoGracePeriodSeconds: state.undoGracePeriodSeconds,
        });
      }
      if (url.pathname.endsWith("/approve")) {
        if (refuseApprovals) {
          return jsonResponse(
            { detail: "This job's points changed." },
            { status: 409 },
          );
        }
        state.approved = true;
        return jsonResponse({
          job: { ...pendingJob, status: "approved" },
          pointsBalance: 5,
        });
      }
      if (url.pathname === "/api/household-settings") {
        if (method === "PUT") {
          const body = JSON.parse(String(state.sent.at(-1)?.body)) as {
            undoGracePeriodSeconds: number;
          };
          if (body.undoGracePeriodSeconds > 120) {
            return jsonResponse(
              {
                title: "Invalid household settings",
                errors: {
                  UndoGracePeriodSeconds: [
                    "Choose whole seconds from 5 to 120.",
                  ],
                },
              },
              { status: 400 },
            );
          }
          state.undoGracePeriodSeconds = body.undoGracePeriodSeconds;
        }
        return jsonResponse({
          undoGracePeriodSeconds: state.undoGracePeriodSeconds,
          minimumUndoGracePeriodSeconds: 5,
          maximumUndoGracePeriodSeconds: 120,
        });
      }
      if (url.pathname === "/api/auth/logout") {
        return new Response(null, { status: 204 });
      }
      if (url.pathname === "/api/auth/refresh") {
        return jsonResponse({ detail: "Signed out." }, { status: 401 });
      }
      if (url.pathname === "/api/auth/start") {
        return jsonResponse({ state: "signIn", members: [] });
      }
      return jsonResponse({ detail: "Not found." }, { status: 404 });
    }),
  );
  return state;
}

function renderApp(viewer = addie) {
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
  render(<RouterProvider router={router} />);
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
}

async function approve(user: ReturnType<typeof userEvent.setup>) {
  await user.click(
    await screen.findByRole("button", { name: "Approve +5 points" }),
  );
  await screen.findByText("Approved — 5 points awarded");
}

describe("undoable actions", () => {
  it("shows an approval as done straight away but sends it only when the household's undo time ends", async () => {
    const api = fakeApi({ undoGracePeriodSeconds: 10 });
    const user = renderApp();

    await approve(user);
    expect(screen.getByLabelText("0 jobs awaiting review")).toBeInTheDocument();
    const toast = screen
      .getByText("Approved “Clear the table” for Fredster: +5 points")
      .closest(".undo-toast") as HTMLElement;
    expect(within(toast).getByText("10s")).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(9_000));
    expect(api.sent).toEqual([]);

    await act(() => vi.advanceTimersByTimeAsync(1_000));
    await waitFor(() =>
      expect(api.sent).toEqual([
        {
          method: "POST",
          path: `/api/jobs/${pendingJob.id}/approve`,
          body: "",
        },
      ]),
    );
    await waitFor(() =>
      expect(screen.queryByRole("button", { name: "Undo" })).toBeNull(),
    );
    expect(screen.getByText("Approved — 5 points awarded")).toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(60_000));
    expect(api.sent).toHaveLength(1);
  });

  it("goes straight from a queued approval to the saved one", async () => {
    fakeApi();
    const user = renderApp();
    await approve(user);

    // Showing the job undone, even for one render, is a flicker.
    const stopRecording = recordChanges(() => [
      ...screen
        .queryAllByLabelText(/jobs awaiting review/)
        .map((count) => count.getAttribute("aria-label") ?? ""),
      ...(screen.queryByRole("button", { name: "Approve +5 points" })
        ? ["Approve +5 points"]
        : []),
    ]);

    await saveQueuedAction(user);
    // Reversing is offered only once the approval is saved and no longer queued.
    expect(
      await screen.findByRole("button", { name: "Reverse approval" }),
    ).toBeInTheDocument();

    expect(stopRecording()).toEqual(["0 jobs awaiting review"]);
  });

  it("puts the job back and sends nothing when Undo is pressed", async () => {
    const api = fakeApi();
    const user = renderApp();

    await approve(user);
    await user.click(screen.getByRole("button", { name: "Undo" }));

    expect(
      await screen.findByRole("button", { name: "Approve +5 points" }),
    ).toBeEnabled();
    expect(screen.getByLabelText("1 jobs awaiting review")).toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(120_000));
    expect(api.sent).toEqual([]);
  });

  it("can't act on a queued job twice", async () => {
    fakeApi();
    const user = renderApp();

    await approve(user);

    expect(
      screen.queryByRole("button", { name: "Approve +5 points" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Reject job" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Edit job")).not.toBeInTheDocument();
  });

  it.each([
    [
      "the tab is hidden",
      () => {
        Object.defineProperty(document, "visibilityState", {
          configurable: true,
          value: "hidden",
        });
        document.dispatchEvent(new Event("visibilitychange"));
      },
    ],
    [
      "the page is closed",
      () => window.dispatchEvent(new PageTransitionEvent("pagehide")),
    ],
  ])("sends queued actions straight away when %s", async (_, hide) => {
    const api = fakeApi({ undoGracePeriodSeconds: 120 });
    const user = renderApp();

    await approve(user);
    expect(api.sent).toEqual([]);
    act(hide);

    await waitFor(() =>
      expect(api.sent.map((request) => request.path)).toEqual([
        `/api/jobs/${pendingJob.id}/approve`,
      ]),
    );
    Object.defineProperty(document, "visibilityState", {
      configurable: true,
      value: "visible",
    });
  });

  it("sends queued actions before signing out", async () => {
    const api = fakeApi({ undoGracePeriodSeconds: 120 });
    const user = renderApp();

    await approve(user);
    await user.click(screen.getByText("Addie", { selector: "summary" }));
    await user.click(screen.getByRole("button", { name: "Sign out" }));

    await waitFor(() =>
      expect(api.sent.map((request) => request.path)).toEqual([
        `/api/jobs/${pendingJob.id}/approve`,
        "/api/auth/logout",
        "/api/auth/refresh",
      ]),
    );
    expect(screen.queryByRole("button", { name: "Undo" })).toBeNull();
  });

  it("lets a grown-up change the undo time for later actions", async () => {
    const api = fakeApi({ undoGracePeriodSeconds: 30 });
    const user = renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(settingsSummary());
    const form = await screen.findByRole("form", { name: "Undo time" });
    const seconds = within(form).getByLabelText("Seconds to undo");
    expect(seconds).toHaveValue(30);
    expect(seconds).toHaveAttribute("min", "5");
    expect(seconds).toHaveAttribute("max", "120");
    await user.clear(seconds);
    await user.type(seconds, "45");
    await user.click(
      within(form).getByRole("button", { name: "Save undo time" }),
    );

    expect(
      await screen.findByText("Undo time saved: 45 seconds on every device.", {
        selector: ".status-toast > span",
      }),
    ).toBeInTheDocument();
    expectAnnounced("Undo time saved: 45 seconds on every device.");
    expect(api.sent).toEqual([
      {
        method: "PUT",
        path: "/api/household-settings",
        body: JSON.stringify({ undoGracePeriodSeconds: 45 }),
      },
    ]);
    // Saving the setting itself happens at once; it isn't an undoable action.
    expect(screen.queryByRole("button", { name: "Undo" })).toBeNull();

    await approve(user);
    const toast = screen
      .getByText("Approved “Clear the table” for Fredster: +5 points")
      .closest(".undo-toast") as HTMLElement;
    expect(within(toast).getByText("45s")).toBeInTheDocument();
  });

  it("follows an undo time another device saved once the board reloads", async () => {
    const api = fakeApi({ undoGracePeriodSeconds: 30 });
    const user = renderApp();
    await screen.findByRole("heading", { name: "Good day, Addie!" });

    api.undoGracePeriodSeconds = 5;
    const loads = vi.mocked(globalThis.fetch).mock.calls.length;
    await user.click(screen.getByRole("link", { name: "Next day →" }));
    await waitFor(() =>
      expect(vi.mocked(globalThis.fetch).mock.calls.length).toBeGreaterThan(
        loads,
      ),
    );
    await approve(user);

    const toast = screen
      .getByText("Approved “Clear the table” for Fredster: +5 points")
      .closest(".undo-toast") as HTMLElement;
    expect(within(toast).getByText("5s")).toBeInTheDocument();
  });

  it("shows the server's reason when an undo time is refused", async () => {
    fakeApi();
    const user = renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(settingsSummary());
    const form = await screen.findByRole("form", { name: "Undo time" });
    const seconds = within(form).getByLabelText("Seconds to undo");
    // Skip the browser's own range check to reach the API's validation.
    seconds.removeAttribute("max");
    await user.clear(seconds);
    await user.type(seconds, "121");
    await user.click(
      within(form).getByRole("button", { name: "Save undo time" }),
    );

    expect(await within(form).findByRole("alert")).toHaveTextContent(
      "Choose whole seconds from 5 to 120.",
    );
  });

  it("doesn't offer children the undo time setting", async () => {
    fakeApi({ viewer: fredster });
    renderApp(fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    expect(document.querySelector(".household-settings")).toBeNull();
  });

  it("withdraws Try again once the person who failed signs out", async () => {
    fakeApi({ refuseApprovals: true });
    const user = renderApp();

    await approve(user);
    await saveQueuedAction(user);
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Not saved: Approved “Clear the table” for Fredster: +5 points. This job's points changed.",
    );
    expect(screen.getByRole("button", { name: "Try again" })).toBeVisible();

    await user.click(screen.getByText("Addie", { selector: "summary" }));
    await user.click(screen.getByRole("button", { name: "Sign out" }));

    await waitFor(() =>
      expect(
        screen.queryByRole("button", { name: "Try again" }),
      ).not.toBeInTheDocument(),
    );
  });

  it("explains a session that lapsed while the action waited", async () => {
    const api = fakeApi();
    const user = renderApp();

    await approve(user);
    // The session ends on the server while the toast counts down.
    acceptSession({
      accessToken: "expired-token",
      accessTokenExpiresAtUtc: "2000-01-01T00:00:00Z",
      member: { id: addie.id, displayName: "Addie", role: "adult" },
    });
    await saveQueuedAction(user);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Not saved: Approved “Clear the table” for Fredster: +5 points. Your session has expired. Sign in again.",
    );
    expect(api.sent.some((request) => request.path.endsWith("/approve"))).toBe(
      false,
    );
  });
});

function settingsSummary(): HTMLElement {
  const summary = document.querySelector<HTMLElement>(
    ".household-settings > summary",
  );
  if (!summary) {
    throw new Error("The undo time setting was not rendered.");
  }
  return summary;
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
