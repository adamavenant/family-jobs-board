import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { routes } from "../../app/routes";

const addie = {
  id: "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c",
  firstName: "Addie",
  nickname: null,
  displayName: "Addie",
  isAdult: true,
};
const hellie = {
  id: "9db319c1-28d1-4ce6-93d7-f04a45f8257d",
  firstName: "Hellie",
  nickname: null,
  displayName: "Hellie",
  isAdult: true,
};
const fredster = {
  id: "754de05d-b6f6-4626-bbad-79e2079cc5c3",
  firstName: "Fredster",
  nickname: null,
  displayName: "Fredster",
  isAdult: false,
};
const harrie = {
  id: "e22facf5-69ce-45ce-9dad-306eef1852c9",
  firstName: "Harrie",
  nickname: null,
  displayName: "Harrie",
  isAdult: false,
};

const board = {
  viewer: addie,
  members: [addie, hellie, fredster, harrie],
  date: "2026-08-29",
  pointsBalance: null,
  pendingApprovalCount: 1,
  pointEarnings: [],
  jobs: [
    {
      id: "7009b529-733c-4770-ae56-1f6fa69f6363",
      childId: fredster.id,
      childDisplayName: fredster.displayName,
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
      childId: fredster.id,
      childDisplayName: fredster.displayName,
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
      childId: fredster.id,
      childDisplayName: fredster.displayName,
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

const childBoard = {
  ...board,
  viewer: fredster,
  pointsBalance: 0,
  jobs: board.jobs,
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

  it("shows the selected adult, family jobs, and review controls", async () => {
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
      screen.getByRole("button", { name: "Approve +5 points" }),
    ).toBeInTheDocument();
    expect(screen.getAllByText("For Fredster")[0]).toBeInTheDocument();
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

  it("adds a job for the selected child", async () => {
    const addedJob = {
      id: "f75612ce-4253-4ca7-8d13-52636e825d98",
      childId: fredster.id,
      childDisplayName: fredster.displayName,
      name: "Put toys away",
      description: "Return every toy to its box.",
      points: 4,
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    };
    const boardWithAddedJob = { ...board, jobs: [...board.jobs, addedJob] };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(jsonResponse(addedJob, { status: 201 }))
        .mockResolvedValueOnce(jsonResponse(boardWithAddedJob)),
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
    expect(
      within(addedCard as HTMLElement).getByText("Ready for Fredster"),
    ).toBeInTheDocument();
  });

  it("creates a daily recurring job and refreshes today's board", async () => {
    const recurringJob = {
      id: "667b50fd-447d-4320-8390-ea82f5bb9145",
      childId: fredster.id,
      childDisplayName: fredster.displayName,
      name: "Feed the fish",
      description: "Add one small scoop.",
      points: 3,
      scheduledDate: board.date,
      agendaPeriod: "morning",
      scheduledTime: "07:30:00",
      recurringJobSeriesId: "56d75d00-3b67-4532-a149-8a388889c9ca",
      recurrenceFrequency: "daily",
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(
          jsonResponse(
            {
              seriesId: recurringJob.recurringJobSeriesId,
              generatedThrough: "2026-10-23",
              occurrenceCount: 56,
            },
            { status: 201 },
          ),
        )
        .mockResolvedValueOnce(
          jsonResponse({ ...board, jobs: [...board.jobs, recurringJob] }),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    const recurringTools = screen.getByText("Routines").closest("details");
    expect(recurringTools).not.toBeNull();
    await user.click(screen.getByText("Routines"));
    await user.type(screen.getByLabelText("Daily job name"), "Feed the fish");
    await user.type(
      screen.getByLabelText("Daily job description"),
      "Add one small scoop.",
    );
    await user.clear(screen.getByLabelText("Daily job points"));
    await user.type(screen.getByLabelText("Daily job points"), "3");
    await user.selectOptions(
      screen.getByLabelText("Daily job part of day"),
      "morning",
    );
    await user.type(
      screen.getByLabelText("Daily job time (optional)"),
      "07:30",
    );
    await user.click(screen.getByRole("button", { name: "Create daily job" }));

    const heading = await screen.findByRole("heading", {
      name: "Feed the fish",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    expect(
      within(card as HTMLElement).getByText("Daily · Morning · 07:30"),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Daily job created through 2026-10-23."),
    ).toHaveAttribute("role", "status");
  });

  it("creates a weekly recurring job for selected weekdays", async () => {
    const recurringJob = {
      id: "372ee9d4-1bfc-4daa-a111-39a29eebcb3c",
      childId: fredster.id,
      childDisplayName: fredster.displayName,
      name: "Pack sports kit",
      description: "Check the kit bag.",
      points: 4,
      scheduledDate: board.date,
      agendaPeriod: "evening",
      scheduledTime: "18:15:00",
      recurringJobSeriesId: "afcd56e4-ed26-4399-b342-673905d55079",
      recurrenceFrequency: "weekly",
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(
          jsonResponse(
            {
              seriesId: recurringJob.recurringJobSeriesId,
              generatedThrough: "2026-10-23",
              occurrenceCount: 16,
            },
            { status: 201 },
          ),
        )
        .mockResolvedValueOnce(
          jsonResponse({ ...board, jobs: [...board.jobs, recurringJob] }),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Routines"));
    await user.selectOptions(screen.getByLabelText("Repeats"), "weekly");
    await user.click(screen.getByRole("checkbox", { name: "Monday" }));
    await user.click(screen.getByRole("checkbox", { name: "Saturday" }));
    await user.type(
      screen.getByLabelText("Weekly job name"),
      "Pack sports kit",
    );
    await user.type(
      screen.getByLabelText("Weekly job description"),
      "Check the kit bag.",
    );
    await user.clear(screen.getByLabelText("Weekly job points"));
    await user.type(screen.getByLabelText("Weekly job points"), "4");
    await user.selectOptions(
      screen.getByLabelText("Weekly job part of day"),
      "evening",
    );
    await user.type(
      screen.getByLabelText("Weekly job time (optional)"),
      "18:15",
    );
    await user.click(screen.getByRole("button", { name: "Create weekly job" }));

    const heading = await screen.findByRole("heading", {
      name: "Pack sports kit",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    expect(
      within(card as HTMLElement).getByText("Weekly · Evening · 18:15"),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Weekly job created through 2026-10-23."),
    ).toHaveAttribute("role", "status");
  });

  it("creates a monthly recurring job for a calendar day", async () => {
    const recurringJob = {
      id: "89201fbb-f714-4910-b68d-c98682a83db2",
      childId: fredster.id,
      childDisplayName: fredster.displayName,
      name: "Clean the fridge",
      description: "Check every shelf.",
      points: 5,
      scheduledDate: board.date,
      agendaPeriod: "morning",
      scheduledTime: "09:15:00",
      recurringJobSeriesId: "ba767aa3-dd5d-49fb-9125-4a17d2da46a8",
      recurrenceFrequency: "monthly",
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(
          jsonResponse(
            {
              seriesId: recurringJob.recurringJobSeriesId,
              generatedThrough: "2026-10-23",
              occurrenceCount: 2,
            },
            { status: 201 },
          ),
        )
        .mockResolvedValueOnce(
          jsonResponse({ ...board, jobs: [...board.jobs, recurringJob] }),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Routines"));
    await user.selectOptions(screen.getByLabelText("Repeats"), "monthly");
    expect(screen.getByLabelText("Day of month")).toHaveValue(29);
    expect(
      screen.getByText("In shorter months, the job runs on the final day."),
    ).toBeInTheDocument();
    await user.type(
      screen.getByLabelText("Monthly job name"),
      "Clean the fridge",
    );
    await user.type(
      screen.getByLabelText("Monthly job description"),
      "Check every shelf.",
    );
    await user.clear(screen.getByLabelText("Monthly job points"));
    await user.type(screen.getByLabelText("Monthly job points"), "5");
    await user.selectOptions(
      screen.getByLabelText("Monthly job part of day"),
      "morning",
    );
    await user.type(
      screen.getByLabelText("Monthly job time (optional)"),
      "09:15",
    );
    await user.click(
      screen.getByRole("button", { name: "Create monthly job" }),
    );

    const heading = await screen.findByRole("heading", {
      name: "Clean the fridge",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    expect(
      within(card as HTMLElement).getByText("Monthly · Morning · 09:15"),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Monthly job created through 2026-10-23."),
    ).toHaveAttribute("role", "status");
  });

  it("reports recurring-job server failures without clearing the form", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(
          jsonResponse(
            { detail: "The recurring job could not be saved." },
            { status: 503 },
          ),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Routines"));
    const name = screen.getByLabelText("Daily job name");
    await user.type(name, "Feed the fish");
    await user.click(screen.getByRole("button", { name: "Create daily job" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The recurring job could not be saved.",
    );
    expect(name).toHaveValue("Feed the fish");
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
      jobs: [board.jobs[0], board.jobs[1], approvedJob],
      pendingApprovalCount: 0,
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

    expect(
      await screen.findByLabelText("0 jobs awaiting review"),
    ).toBeInTheDocument();
    expect(
      await screen.findByText("Approved — 5 points awarded"),
    ).toBeInTheDocument();
    expect(screen.queryByText("How Addie earned them")).not.toBeInTheDocument();
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
    const childRejectedBoard = {
      ...childBoard,
      jobs: [board.jobs[0], board.jobs[1], rejectedJob],
    };
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(jsonResponse(rejectedJob))
        .mockResolvedValueOnce(jsonResponse(rejectedBoard))
        .mockResolvedValueOnce(jsonResponse(childRejectedBoard)),
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

    await user.selectOptions(screen.getByLabelText("Viewing as"), fredster.id);
    const childHeading = await screen.findByRole("heading", {
      name: "Clear the table",
    });
    const childCard = childHeading.closest("article");
    expect(childCard).not.toBeNull();
    expect(
      within(childCard as HTMLElement).getByText("Needs another go"),
    ).toBeInTheDocument();
    expect(
      within(childCard as HTMLElement).getByText(
        "Please wipe underneath the table.",
      ),
    ).toBeInTheDocument();
    expect(
      within(childCard as HTMLElement).getByRole("button", {
        name: "Mark as done",
      }),
    ).toBeInTheDocument();
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

    expect(
      await within(card as HTMLElement).findByRole("alert"),
    ).toHaveTextContent("This job is no longer pending approval.");
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
      .mockResolvedValueOnce(jsonResponse(childBoard))
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

  it("switches to a child view and persists the selection", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(jsonResponse(childBoard)),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.selectOptions(screen.getByLabelText("Viewing as"), fredster.id);

    expect(
      await screen.findByRole("heading", { name: "Good day, Fredster!" }),
    ).toBeInTheDocument();
    expect(window.localStorage.getItem("family-jobs-board-member")).toBe(
      fredster.id,
    );
    expect(screen.queryByText("Grown-up tools")).not.toBeInTheDocument();
    expect(screen.getByLabelText("0 points earned")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Approve/ }),
    ).not.toBeInTheDocument();
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
