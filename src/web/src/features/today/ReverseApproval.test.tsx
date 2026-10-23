import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { acceptSession, clearIdentity } from "../../api/auth";
import { routes } from "../../app/routes";
import { expectAnnounced, saveQueuedAction } from "../../test/undo";

const addie = {
  id: "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c",
  firstName: "Addie",
  nickname: null,
  displayName: "Addie",
  isAdult: true,
};
const fredster = {
  id: "754de05d-b6f6-4626-bbad-79e2079cc5c3",
  firstName: "Fredster",
  nickname: null,
  displayName: "Fredster",
  isAdult: false,
};
const approvedJob = {
  id: "7009b529-733c-4770-ae56-1f6fa69f6363",
  childId: fredster.id,
  childDisplayName: fredster.displayName,
  name: "Feed the dog",
  description: "Fill the food bowl.",
  points: 5,
  scheduledDate: "2026-10-14",
  agendaPeriod: "morning",
  scheduledTime: null,
  recurringJobSeriesId: null,
  recurrenceFrequency: null,
  status: "approved",
  completedAtUtc: "2026-10-14T07:00:00Z",
  approvedAtUtc: "2026-10-14T08:00:00Z",
  latestRejection: null,
};
const pendingJob = {
  ...approvedJob,
  status: "pendingApproval",
  approvedAtUtc: null,
};

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
});

describe("Reversing an approval", () => {
  it("asks first, naming the child and points, with focus on keeping the approval", async () => {
    const fetch = vi.fn().mockResolvedValue(jsonResponse(board([approvedJob])));
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp(addie);

    const card = await jobCard("Feed the dog");
    expect(card).toHaveTextContent("Approved — 5 points awarded");
    await user.click(
      within(card).getByRole("button", { name: "Reverse approval" }),
    );

    const prompt = within(card).getByRole("group", {
      name: "Confirm reversing the approval for Fredster",
    });
    expect(prompt).toHaveTextContent(
      "Reverse this approval and take 5 points back from Fredster?",
    );
    expect(
      within(prompt).getByRole("button", { name: "Keep approved" }),
    ).toHaveFocus();
    expect(fetch).toHaveBeenCalledTimes(1);

    await user.keyboard("{Enter}");

    expect(
      within(card).queryByRole("group", {
        name: "Confirm reversing the approval for Fredster",
      }),
    ).not.toBeInTheDocument();
    expect(
      within(card).getByRole("button", { name: "Reverse approval" }),
    ).toHaveFocus();
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("reverses with the confirmed points and shows the job waiting for approval again", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(board([approvedJob])))
      .mockResolvedValueOnce(
        jsonResponse({ job: pendingJob, pointsBalance: 0 }),
      )
      .mockResolvedValueOnce(
        jsonResponse(board([pendingJob], { pendingApprovalCount: 1 })),
      );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp(addie);

    const card = await jobCard("Feed the dog");
    await user.click(
      within(card).getByRole("button", { name: "Reverse approval" }),
    );
    await user.click(
      within(card).getByRole("button", { name: "Yes, reverse approval" }),
    );

    // The reversal shows at once and waits behind its undo toast.
    expect(
      await screen.findByRole("button", { name: "Approve +5 points" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Feed the dog" })).toHaveFocus();
    expect(
      screen.queryByText("Approved — 5 points awarded"),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Reverse approval" }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "1 jobs awaiting review" }),
    ).toBeInTheDocument();
    expectAnnounced(
      "Reversed approval of “Feed the dog” for Fredster: −5 points",
    );
    expect(fetch).toHaveBeenCalledTimes(1);

    await saveQueuedAction(user);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(3));
    const request = fetch.mock.calls[1]?.[0] as Request;
    expect(request.method).toBe("POST");
    const url = new URL(request.url);
    expect(url.pathname).toBe(`/api/jobs/${approvedJob.id}/reverse-approval`);
    expect(url.searchParams.get("expectedPoints")).toBe("5");
  });

  it("explains a refused reversal and shows the job approved again", async () => {
    const message =
      "Fredster only has 2 points, so reversing this approval can't take back its 5 points. Nothing was changed.";
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(board([approvedJob])))
      .mockResolvedValueOnce(
        jsonResponse(
          {
            title: "Not enough points",
            detail: message,
            status: 409,
            code: "insufficientPoints",
            currentBalance: 2,
          },
          { status: 409 },
        ),
      )
      .mockResolvedValueOnce(jsonResponse(board([approvedJob])));
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp(addie);

    const card = await jobCard("Feed the dog");
    await user.click(
      within(card).getByRole("button", { name: "Reverse approval" }),
    );
    await user.click(
      within(card).getByRole("button", { name: "Yes, reverse approval" }),
    );
    await saveQueuedAction(user);

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(3));
    expect(
      await within(card).findByText("Approved — 5 points awarded"),
    ).toBeInTheDocument();
    expect(
      within(card).getByRole("button", { name: "Reverse approval" }),
    ).toBeEnabled();
  });

  it("shows the job as it is now when another grown-up already reversed it", async () => {
    const message =
      "This job isn't approved any more, so there's no approval to reverse. Refresh to see its latest state.";
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(board([approvedJob])))
      .mockResolvedValueOnce(
        jsonResponse(
          {
            title: "Approval cannot be reversed",
            detail: message,
            status: 409,
          },
          { status: 409 },
        ),
      )
      .mockResolvedValueOnce(
        jsonResponse(board([pendingJob], { pendingApprovalCount: 1 })),
      );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp(addie);

    const card = await jobCard("Feed the dog");
    await user.click(
      within(card).getByRole("button", { name: "Reverse approval" }),
    );
    await user.click(
      within(card).getByRole("button", { name: "Yes, reverse approval" }),
    );
    await saveQueuedAction(user);

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(3));
    expect(
      await within(card).findByRole("button", { name: "Approve +5 points" }),
    ).toBeEnabled();
    expect(card).not.toHaveTextContent("Approved — 5 points awarded");
  });

  it("backs out with Escape and returns focus to the action", async () => {
    const fetch = vi.fn().mockResolvedValue(jsonResponse(board([approvedJob])));
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp(addie);

    const card = await jobCard("Feed the dog");
    await user.click(
      within(card).getByRole("button", { name: "Reverse approval" }),
    );
    await user.keyboard("{Escape}");

    expect(
      within(card).queryByRole("group", {
        name: "Confirm reversing the approval for Fredster",
      }),
    ).not.toBeInTheDocument();
    expect(
      within(card).getByRole("button", { name: "Reverse approval" }),
    ).toHaveFocus();
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("closes the prompt when revalidation changes the job's points", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(board([approvedJob])))
      .mockResolvedValueOnce(
        jsonResponse(board([{ ...approvedJob, points: 7 }])),
      );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    const { router } = renderApp(addie);

    const card = await jobCard("Feed the dog");
    await user.click(
      within(card).getByRole("button", { name: "Reverse approval" }),
    );
    await router.revalidate();

    expect(
      await within(card).findByText("Approved — 7 points awarded"),
    ).toBeInTheDocument();
    expect(
      within(card).queryByRole("group", {
        name: "Confirm reversing the approval for Fredster",
      }),
    ).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(2);
  });

  it("isn't offered to children", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse(board([approvedJob], { viewer: fredster })),
        ),
    );
    renderApp(fredster);

    const card = await jobCard("Feed the dog");
    expect(card).toHaveTextContent("Approved — 5 points awarded");
    expect(
      within(card).queryByRole("button", { name: "Reverse approval" }),
    ).not.toBeInTheDocument();
  });
});

async function jobCard(name: string): Promise<HTMLElement> {
  const heading = await screen.findByRole("heading", { name });
  const card = heading.closest("article");
  if (!card) {
    throw new Error(`The ${name} card was not rendered.`);
  }

  return card;
}

function board(
  jobs: unknown[],
  overrides: { viewer?: typeof addie; pendingApprovalCount?: number } = {},
) {
  const viewer = overrides.viewer ?? addie;
  return {
    viewer,
    members: [addie, fredster],
    date: "2026-10-14",
    currentDate: "2026-10-14",
    selectedChildId: null,
    jobs,
    pointsBalance: viewer.isAdult ? null : 5,
    pendingApprovalCount: overrides.pendingApprovalCount ?? 0,
    whoseTurns: [],
  };
}

function renderApp(viewer: typeof addie) {
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
  return { router, ...render(<RouterProvider router={router} />) };
}

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
}
