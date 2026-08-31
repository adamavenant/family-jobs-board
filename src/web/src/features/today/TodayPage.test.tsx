import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { routes } from "../../app/routes";

const board = {
  child: {
    id: "c7b3309f-c84c-4b90-b923-305597484642",
    name: "Alex",
    pointsBalance: 0,
  },
  date: "2026-08-29",
  jobs: [
    {
      id: "7009b529-733c-4770-ae56-1f6fa69f6363",
      name: "Feed the dog",
      description: "Fill the food bowl and make sure there is fresh water.",
      points: 5,
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
    },
    {
      id: "b9d6a90c-58e4-4606-bf65-61de33c2573d",
      name: "Pack school bag",
      description: "Check tomorrow's timetable and pack everything needed.",
      points: 8,
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
    },
    {
      id: "ea64b5d3-ab18-4c75-bc33-eb3cbf7524f6",
      name: "Clear the table",
      description: "Take dishes to the kitchen after dinner.",
      points: 5,
      status: "pendingApproval",
      completedAtUtc: "2026-08-29T09:00:00Z",
      approvedAtUtc: null,
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
      await screen.findByRole("heading", { name: "Good day, Alex!" }),
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

    await screen.findByRole("heading", { name: "Good day, Alex!" });
    await user.type(screen.getByLabelText("Job name"), "Put toys away");
    await user.type(
      screen.getByLabelText("Description"),
      "Return every toy to its box.",
    );
    await user.type(screen.getByLabelText("Points"), "4");
    await user.click(screen.getByRole("button", { name: "Add job" }));

    const addedHeading = await screen.findByRole("heading", {
      name: "Put toys away",
    });
    expect(screen.getByText("Job added to today’s board.")).toHaveAttribute(
      "role",
      "status",
    );
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
    const approvedJob = {
      ...pendingJob,
      status: "approved",
      approvedAtUtc: "2026-08-29T10:30:00Z",
    };
    const approvedBoard = {
      ...board,
      child: { ...board.child, pointsBalance: 5 },
      jobs: [board.jobs[0], board.jobs[1], approvedJob],
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
  });

  it("persists an explicit dark theme selection", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(() => Promise.resolve(jsonResponse(board))),
    );
    const user = userEvent.setup();
    const rendered = renderApp();

    await screen.findByRole("heading", { name: "Good day, Alex!" });
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

    await screen.findByRole("heading", { name: "Good day, Alex!" });
    const name = screen.getByLabelText("Job name");
    await user.type(name, "Put toys away");
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

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
    ...init,
  });
}
