import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { routes } from "../../app/routes";

const board = {
  child: {
    id: "c7b3309f-c84c-4b90-b923-305597484642",
    firstName: "Addie",
    nickname: null,
    displayName: "Addie",
    pointsBalance: 0,
  },
  date: "2026-08-29",
  pointEarnings: [],
  jobs: [
    {
      id: "7009b529-733c-4770-ae56-1f6fa69f6363",
      name: "Feed the dog",
      description: "Fill the food bowl and make sure there is fresh water.",
      points: 5,
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    },
    {
      id: "b9d6a90c-58e4-4606-bf65-61de33c2573d",
      name: "Pack school bag",
      description: "Check tomorrow's timetable and pack everything needed.",
      points: 8,
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    },
    {
      id: "ea64b5d3-ab18-4c75-bc33-eb3cbf7524f6",
      name: "Clear the table",
      description: "Take dishes to the kitchen after dinner.",
      points: 5,
      status: "pendingApproval",
      completedAtUtc: "2026-08-29T09:00:00Z",
      approvedAtUtc: null,
      latestRejection: null,
    },
  ],
};

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
  delete document.documentElement.dataset.theme;
});

describe("Today page", () => {
  it("shows a loading state while today's board is requested", () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => new Promise(() => undefined)),
    );

    renderApp();

    expect(screen.getByText("Getting today’s jobs ready…")).toBeInTheDocument();
  });

  it("shows the child, jobs, and completion states", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(board)));

    renderApp();

    expect(
      await screen.findByRole("heading", { name: "Good day, Addie!" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Feed the dog" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: "Pack school bag" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Nice work — ready for a grown-up."),
    ).toBeInTheDocument();
    expect(
      screen.getByText("No points earned yet.", { exact: false }),
    ).toBeInTheDocument();
  });

  it("keeps grown-up tools collapsed until requested and defaults points to one", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(board)));
    const user = userEvent.setup();

    renderApp();
    await screen.findByRole("heading", { name: "Good day, Addie!" });

    const tools = screen.getByText("Grown-up tools").closest("details");
    expect(tools).not.toBeNull();
    expect(tools).not.toHaveAttribute("open");
    expect(screen.getByRole("textbox", { name: "Job name" })).not.toBeVisible();

    await user.click(within(tools as HTMLElement).getByText("Add a job"));

    expect(tools).toHaveAttribute("open");
    expect(screen.getByRole("spinbutton", { name: "Points" })).toHaveValue(1);
  });

  it("adds a job to the board and allows it to be completed", async () => {
    const addedJob = {
      id: "f75612ce-4253-4ca7-8d13-52636e825d98",
      name: "Put toys away",
      description: "Return every toy to its box.",
      points: 4,
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    };
    const boardWithAddedJob = { ...board, jobs: [...board.jobs, addedJob] };
    const completedJob = {
      ...addedJob,
      status: "pendingApproval",
      completedAtUtc: "2026-08-29T10:00:00Z",
      approvedAtUtc: null,
    };
    const completedBoard = {
      ...board,
      jobs: [...board.jobs, completedJob],
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(jsonResponse(addedJob, { status: 201 }))
        .mockResolvedValueOnce(jsonResponse(boardWithAddedJob))
        .mockResolvedValueOnce(jsonResponse(completedJob))
        .mockResolvedValueOnce(jsonResponse(completedBoard)),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await openGrownUpTools(user);
    await user.type(screen.getByLabelText("Job name"), "Put toys away");
    await user.type(
      screen.getByLabelText("Description"),
      "Return every toy to its box.",
    );
    await user.clear(screen.getByLabelText("Points"));
    await user.type(screen.getByLabelText("Points"), "4");
    await user.click(screen.getByRole("button", { name: "Add job" }));

    const addedHeading = await screen.findByRole("heading", {
      name: "Put toys away",
    });
    expect(screen.getByText("Job added to today’s board.")).toHaveAttribute(
      "role",
      "status",
    );
    expect(screen.getByLabelText("Points")).toHaveValue(1);
    const addedCard = addedHeading.closest("article");
    expect(addedCard).not.toBeNull();

    await user.click(
      within(addedCard as HTMLElement).getByRole("button", {
        name: "Mark as done",
      }),
    );

    expect(
      await within(addedCard as HTMLElement).findByText(
        "Nice work — ready for a grown-up.",
      ),
    ).toBeInTheDocument();
  });

  it("approves a pending job and updates the points balance", async () => {
    const pendingJob = board.jobs[2];
    if (!pendingJob) {
      throw new Error("The pending-job fixture was missing.");
    }
    const approvedJob = {
      ...pendingJob,
      status: "approved",
      approvedAtUtc: "2026-08-29T10:30:00Z",
    };
    const approvedBoard = {
      ...board,
      child: { ...board.child, pointsBalance: 5 },
      jobs: [board.jobs[0], board.jobs[1], approvedJob],
      pointEarnings: [
        {
          id: "63fd708b-1296-409d-9ae4-7cd6fc501af7",
          jobId: pendingJob.id,
          jobName: pendingJob.name,
          points: 5,
          awardedAtUtc: "2026-08-29T10:30:00Z",
        },
      ],
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(
          jsonResponse({ job: approvedJob, pointsBalance: 5 }),
        )
        .mockResolvedValueOnce(jsonResponse(approvedBoard)),
    );
    const user = userEvent.setup();
    renderApp();

    const heading = await screen.findByRole("heading", {
      name: "Clear the table",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    await user.click(
      within(card as HTMLElement).getByRole("button", {
        name: "Approve +5 points",
      }),
    );

    expect(await screen.findByLabelText("5 points earned")).toBeInTheDocument();
    expect(
      await screen.findByText("Approved — 5 points awarded"),
    ).toBeInTheDocument();
    const history = screen
      .getByRole("heading", { name: "How Addie earned them" })
      .closest("section");
    expect(history).not.toBeNull();
    expect(within(history as HTMLElement).getByText("+5")).toBeInTheDocument();
    expect(
      within(history as HTMLElement).getByText("Clear the table"),
    ).toBeInTheDocument();
  });

  it("rejects a pending job with feedback and allows another try", async () => {
    const pendingJob = board.jobs[2];
    if (!pendingJob) {
      throw new Error("The pending-job fixture was missing.");
    }
    const rejectedJob = {
      ...pendingJob,
      status: "open",
      completedAtUtc: null,
      latestRejection: {
        decisionId: "c01e1d57-826e-4eb6-978a-72dcfe2bbc8a",
        reason: "Please wipe underneath the table.",
        rejectedAtUtc: "2026-08-29T10:15:00Z",
      },
    };
    const rejectedBoard = {
      ...board,
      jobs: [board.jobs[0], board.jobs[1], rejectedJob],
    };
    const resubmittedJob = {
      ...rejectedJob,
      status: "pendingApproval",
      completedAtUtc: "2026-08-29T10:30:00Z",
      latestRejection: null,
    };
    const resubmittedBoard = {
      ...board,
      jobs: [board.jobs[0], board.jobs[1], resubmittedJob],
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(jsonResponse(rejectedJob))
        .mockResolvedValueOnce(jsonResponse(rejectedBoard))
        .mockResolvedValueOnce(jsonResponse(resubmittedJob))
        .mockResolvedValueOnce(jsonResponse(resubmittedBoard)),
    );
    const user = userEvent.setup();
    renderApp();

    const heading = await screen.findByRole("heading", {
      name: "Clear the table",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    const reason = within(card as HTMLElement).getByRole("textbox", {
      name: "Rejection reason (optional)",
    });
    await user.type(reason, "Please wipe underneath the table.");
    await user.click(
      within(card as HTMLElement).getByRole("button", { name: "Reject job" }),
    );

    expect(
      await within(card as HTMLElement).findByText("Needs another go"),
    ).toBeInTheDocument();
    expect(
      within(card as HTMLElement).getByText(
        "Please wipe underneath the table.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("0 points earned")).toBeInTheDocument();

    await user.click(
      within(card as HTMLElement).getByRole("button", { name: "Mark as done" }),
    );

    expect(
      await within(card as HTMLElement).findByText(
        "Nice work — ready for a grown-up.",
      ),
    ).toBeInTheDocument();
    expect(
      within(card as HTMLElement).queryByText("Needs another go"),
    ).not.toBeInTheDocument();
  });

  it("reports rejection failures without clearing the reason", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(
          jsonResponse(
            { detail: "This job is no longer pending approval." },
            { status: 409 },
          ),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    const heading = await screen.findByRole("heading", {
      name: "Clear the table",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    const reason = within(card as HTMLElement).getByRole("textbox", {
      name: "Rejection reason (optional)",
    });
    await user.type(reason, "Please try again.");
    await user.click(
      within(card as HTMLElement).getByRole("button", { name: "Reject job" }),
    );

    expect(await within(card as HTMLElement).findByRole("alert")).toHaveTextContent(
      "This job is no longer pending approval.",
    );
    expect(reason).toHaveValue("Please try again.");
  });

  it("persists an explicit dark theme selection", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(board))),
    );
    const user = userEvent.setup();
    const rendered = renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByRole("button", { name: "Dark mode" }));

    expect(document.documentElement).toHaveAttribute("data-theme", "dark");
    expect(window.localStorage.getItem("family-jobs-board-theme")).toBe("dark");

    rendered.unmount();
    document.documentElement.dataset.theme =
      window.localStorage.getItem("family-jobs-board-theme") ?? "light";
    renderApp();

    expect(
      await screen.findByRole("button", { name: "Light mode" }),
    ).toBePressed();
  });

  it("reports creation failures without removing the entered job", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(
          jsonResponse(
            { detail: "The database is unavailable." },
            { status: 503 },
          ),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await openGrownUpTools(user);
    const name = screen.getByLabelText("Job name");
    await user.type(name, "Put toys away");
    await user.clear(screen.getByLabelText("Points"));
    await user.type(screen.getByLabelText("Points"), "4");
    await user.click(screen.getByRole("button", { name: "Add job" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The database is unavailable.",
    );
    expect(name).toHaveValue("Put toys away");
  });

  it("reports completion failures through the job card", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(board))
      .mockResolvedValueOnce(
        jsonResponse(
          { detail: "This job is already pending approval." },
          { status: 409 },
        ),
      );
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderApp();

    const heading = await screen.findByRole("heading", {
      name: "Feed the dog",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    await user.click(
      within(card as HTMLElement).getByRole("button", { name: "Mark as done" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "This job is already pending approval.",
    );
  });
});

function renderApp() {
  const router = createMemoryRouter(routes, { initialEntries: ["/"] });
  return render(<RouterProvider router={router} />);
}

async function openGrownUpTools(user: ReturnType<typeof userEvent.setup>) {
  const tools = screen.getByText("Grown-up tools").closest("details");
  if (!tools) {
    throw new Error("Grown-up tools were not rendered.");
  }

  await user.click(within(tools).getByText("Add a job"));
}

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
}
