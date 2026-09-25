import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { routes } from "../../app/routes";
import { acceptSession, clearIdentity } from "../../api/auth";
import type { FamilyMember } from "../../api/members";

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
  currentDate: "2026-08-29",
  selectedChildId: null,
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
  clearIdentity();
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

  it("lets an adult edit an open job and refreshes the agenda", async () => {
    const original = {
      ...board.jobs[0],
      scheduledDate: board.date,
      agendaPeriod: "morning",
      scheduledTime: null,
      recurringJobSeriesId: null,
      recurrenceFrequency: null,
    };
    const initialBoard = { ...board, jobs: [original] };
    const updated = {
      ...original,
      name: "Feed and water the dog",
      description: "Fresh water and one scoop.",
      points: 7,
      agendaPeriod: "evening",
      scheduledTime: "18:15:00",
    };
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(initialBoard))
      .mockResolvedValueOnce(jsonResponse(updated))
      .mockResolvedValueOnce(
        jsonResponse({ ...initialBoard, jobs: [updated] }),
      );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    const heading = await screen.findByRole("heading", {
      name: "Feed the dog",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    await user.click(within(card as HTMLElement).getByText("Edit job"));
    const name = within(card as HTMLElement).getByLabelText("Edit job name");
    await user.clear(name);
    await user.type(name, "Feed and water the dog");
    await user.clear(
      within(card as HTMLElement).getByLabelText("Edit description"),
    );
    await user.type(
      within(card as HTMLElement).getByLabelText("Edit description"),
      "Fresh water and one scoop.",
    );
    await user.clear(within(card as HTMLElement).getByLabelText("Edit points"));
    await user.type(
      within(card as HTMLElement).getByLabelText("Edit points"),
      "7",
    );
    await user.selectOptions(
      within(card as HTMLElement).getByLabelText("Edit part of day"),
      "evening",
    );
    await user.type(
      within(card as HTMLElement).getByLabelText("Edit time (optional)"),
      "18:15",
    );
    await user.click(
      within(card as HTMLElement).getByRole("button", { name: "Save changes" }),
    );

    expect(
      await screen.findByRole("heading", { name: "Feed and water the dog" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Evening · 18:15")).toBeInTheDocument();
    const updateRequest = fetch.mock.calls[1]?.[0];
    expect(updateRequest).toBeInstanceOf(Request);
    expect((updateRequest as Request).method).toBe("PUT");
    expect(await (updateRequest as Request).clone().json()).toMatchObject({
      name: "Feed and water the dog",
      points: 7,
      agendaPeriod: "evening",
      scheduledTime: "18:15",
    });
  });

  it("lets an adult cancel a pending job and removes it from the agenda", async () => {
    const pending = {
      ...board.jobs[2],
      scheduledDate: board.date,
      agendaPeriod: "evening",
      scheduledTime: null,
      recurringJobSeriesId: null,
      recurrenceFrequency: null,
    };
    const initialBoard = { ...board, jobs: [pending] };
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(initialBoard))
      .mockResolvedValueOnce(jsonResponse({ ...pending, status: "cancelled" }))
      .mockResolvedValueOnce(
        jsonResponse({ ...initialBoard, jobs: [], pendingApprovalCount: 0 }),
      );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    const heading = await screen.findByRole("heading", {
      name: "Clear the table",
    });
    const card = heading.closest("article");
    expect(card).not.toBeNull();
    await user.click(within(card as HTMLElement).getByText("Cancel job"));
    await user.type(
      within(card as HTMLElement).getByLabelText(
        "Cancellation reason (optional)",
      ),
      "No longer needed.",
    );
    await user.click(
      within(card as HTMLElement).getByRole("button", {
        name: "Cancel this job",
      }),
    );

    expect(
      await screen.findByText("No jobs are scheduled for this day."),
    ).toBeVisible();
    expect(
      screen.queryByRole("heading", { name: "Clear the table" }),
    ).not.toBeInTheDocument();
    const cancelRequest = fetch.mock.calls[1]?.[0];
    expect(cancelRequest).toBeInstanceOf(Request);
    expect(await (cancelRequest as Request).clone().json()).toEqual({
      reason: "No longer needed.",
    });
  });

  it("does not show job management controls to a child", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(childBoard)));
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    expect(screen.queryByText("Edit job")).not.toBeInTheDocument();
    expect(screen.queryByText("Cancel job")).not.toBeInTheDocument();
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

  it("requires at least one child for a new job", async () => {
    const fetch = vi.fn().mockResolvedValue(jsonResponse(board));
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();

    renderApp();
    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await openGrownUpTools(user);
    const tools = screen.getByText("Grown-up tools").closest("details");
    expect(tools).not.toBeNull();
    await user.click(
      within(tools as HTMLElement).getByRole("checkbox", {
        name: "Fredster",
      }),
    );
    await user.type(
      screen.getByRole("textbox", { name: "Job name" }),
      "Tidy up",
    );
    await user.click(screen.getByRole("button", { name: "Add job" }));

    expect(
      await screen.findByText("Choose one or more children."),
    ).toHaveAttribute("role", "alert");
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("adds a job for the selected child", async () => {
    const addedJob = {
      id: "f75612ce-4253-4ca7-8d13-52636e825d98",
      childId: fredster.id,
      childDisplayName: fredster.displayName,
      name: "Put toys away",
      description: "Return every toy to its box.",
      points: 4,
      scheduledDate: board.date,
      agendaPeriod: "arrivingHome",
      scheduledTime: "15:45:00",
      recurringJobSeriesId: null,
      recurrenceFrequency: null,
      status: "open",
      completedAtUtc: null,
      approvedAtUtc: null,
      latestRejection: null,
    };
    const boardWithAddedJob = { ...board, jobs: [...board.jobs, addedJob] };
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(board))
      .mockResolvedValueOnce(
        jsonResponse({ jobs: [addedJob] }, { status: 201 }),
      )
      .mockResolvedValueOnce(jsonResponse(boardWithAddedJob));
    vi.stubGlobal("fetch", fetch);
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
    await user.selectOptions(
      screen.getByLabelText("Part of day"),
      "arrivingHome",
    );
    await user.type(screen.getByLabelText("Time (optional)"), "15:45");
    await user.click(screen.getByRole("button", { name: "Add job" }));

    const addedHeading = await screen.findByRole("heading", {
      name: "Put toys away",
    });
    expect(
      await screen.findByText("Job scheduled for 2026-08-29."),
    ).toHaveAttribute("role", "status");
    expect(screen.getByLabelText("Points")).toHaveValue(1);
    const addedCard = addedHeading.closest("article");
    expect(addedCard).not.toBeNull();
    expect(
      within(addedCard as HTMLElement).getByText("Ready for Fredster"),
    ).toBeInTheDocument();
    expect(
      within(addedCard as HTMLElement).getByText("Arriving home · 15:45"),
    ).toBeInTheDocument();
    const createRequest = fetch.mock.calls.find(([input]) => {
      const request = input instanceof Request ? input : null;
      return (
        requestPath(input) === "/api/today/jobs" && request?.method === "POST"
      );
    })?.[0];
    expect(createRequest).toBeInstanceOf(Request);
    expect(await (createRequest as Request).clone().json()).toEqual({
      childIds: [fredster.id],
      name: "Put toys away",
      description: "Return every toy to its box.",
      points: 4,
      scheduledDate: board.date,
      agendaPeriod: "arrivingHome",
      scheduledTime: "15:45",
    });
  });

  it("browses another day and returns to today", async () => {
    const nextDate = "2026-08-30";
    const nextBoard = {
      ...board,
      date: nextDate,
      jobs: [],
    };
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = requestUrl(input);
      return Promise.resolve(
        jsonResponse(
          url.searchParams.get("date") === nextDate ? nextBoard : board,
        ),
      );
    });
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByRole("link", { name: "Next day →" }));

    expect(await screen.findByText("Sunday, August 30")).toBeInTheDocument();
    expect(
      screen.getByText("No jobs are scheduled for this day."),
    ).toBeVisible();
    expect(screen.getByRole("link", { name: "Today" })).toHaveAttribute(
      "href",
      "/",
    );
    expect(
      fetch.mock.calls.some(([input]) =>
        requestUrl(input).searchParams.has("date", nextDate),
      ),
    ).toBe(true);
  });

  it("filters the adult agenda by child and keeps the filter across dates", async () => {
    const nextDate = "2026-08-30";
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = requestUrl(input);
      const childId = url.searchParams.get("childId");
      return Promise.resolve(
        jsonResponse({
          ...board,
          date: url.searchParams.get("date") ?? board.currentDate,
          selectedChildId: childId,
          jobs: childId === harrie.id ? [] : board.jobs,
        }),
      );
    });
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    acceptSession({
      accessToken: "test-access-token",
      accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
      member: { id: addie.id, displayName: addie.displayName, role: "adult" },
    });
    const router = createMemoryRouter(routes, { initialEntries: ["/"] });
    render(<RouterProvider router={router} />);

    await screen.findByRole("heading", { name: "Family jobs" });
    await user.click(screen.getByRole("link", { name: "Harrie" }));

    expect(
      await screen.findByRole("heading", { name: "Harrie’s jobs" }),
    ).toBeVisible();
    expect(
      screen.getByText("No jobs are scheduled for Harrie on this day."),
    ).toBeVisible();
    expect(screen.getByLabelText("0 jobs on this day")).toBeVisible();
    expect(screen.getByLabelText("1 jobs awaiting review")).toBeVisible();
    expect(router.state.location.search).toBe(`?childId=${harrie.id}`);

    await user.click(screen.getByRole("link", { name: "Next day →" }));

    expect(await screen.findByText("Sunday, August 30")).toBeVisible();
    expect(router.state.location.search).toBe(
      `?date=${nextDate}&childId=${harrie.id}`,
    );
    expect(
      fetch.mock.calls.some(([input]) => {
        const url = requestUrl(input);
        return (
          url.searchParams.get("date") === nextDate &&
          url.searchParams.get("childId") === harrie.id
        );
      }),
    ).toBe(true);

    await user.click(screen.getByRole("link", { name: "Today" }));
    expect(router.state.location.search).toBe(`?childId=${harrie.id}`);
    await user.click(screen.getByRole("link", { name: "All children" }));
    expect(
      await screen.findByRole("heading", { name: "Family jobs" }),
    ).toBeVisible();
    expect(router.state.location.search).toBe("");
  });

  it("assigns one job to both children with an accessible multi-selection", async () => {
    const jobs = [
      {
        ...board.jobs[0],
        id: "012ece40-d2df-496b-ae4b-99102aa24880",
        childId: fredster.id,
        childDisplayName: fredster.displayName,
        name: "Make the beds",
      },
      {
        ...board.jobs[0],
        id: "a0e4556e-0ea4-40e2-8e8f-58a03d13e907",
        childId: harrie.id,
        childDisplayName: harrie.displayName,
        name: "Make the beds",
      },
    ];
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(jsonResponse({ jobs }, { status: 201 }))
        .mockResolvedValueOnce(
          jsonResponse({ ...board, jobs: [...board.jobs, ...jobs] }),
        ),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await openGrownUpTools(user);
    const oneOffTools = screen.getByText("Grown-up tools").closest("details");
    expect(oneOffTools).not.toBeNull();
    const fredsterOption = within(oneOffTools as HTMLElement).getByRole(
      "checkbox",
      { name: "Fredster" },
    );
    const harrieOption = within(oneOffTools as HTMLElement).getByRole(
      "checkbox",
      { name: "Harrie" },
    );
    expect(fredsterOption).toBeChecked();
    expect(harrieOption).not.toBeChecked();
    await user.click(harrieOption);
    await user.type(screen.getByLabelText("Job name"), "Make the beds");
    await user.click(screen.getByRole("button", { name: "Add job" }));

    expect(
      await screen.findAllByRole("heading", { name: "Make the beds" }),
    ).toHaveLength(2);
    expect(screen.getAllByText("Ready for Fredster").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Ready for Harrie")).toHaveLength(1);
  });

  it.each(["native", "fallback"])(
    "creates a daily recurring job and refreshes today's board (%s UUID)",
    async (mode) => {
      if (mode === "fallback") {
        vi.stubGlobal("crypto", {
          getRandomValues: crypto.getRandomValues.bind(crypto),
        });
      }
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
      const harrieRecurringJob = {
        ...recurringJob,
        id: "8a9ea890-7f2c-48d4-a08a-3fdccad19a53",
        childId: harrie.id,
        childDisplayName: harrie.displayName,
        recurringJobSeriesId: "a40700dd-17a9-4733-8207-4fc4096516e3",
      };
      vi.stubGlobal(
        "fetch",
        vi
          .fn()
          .mockResolvedValueOnce(jsonResponse(board))
          .mockResolvedValueOnce(
            jsonResponse(
              {
                assignments: [
                  {
                    seriesId: recurringJob.recurringJobSeriesId,
                    childId: fredster.id,
                    generatedThrough: "2026-10-23",
                    occurrenceCount: 56,
                  },
                  {
                    seriesId: harrieRecurringJob.recurringJobSeriesId,
                    childId: harrie.id,
                    generatedThrough: "2026-10-23",
                    occurrenceCount: 56,
                  },
                ],
              },
              { status: 201 },
            ),
          )
          .mockResolvedValueOnce(
            jsonResponse({
              ...board,
              jobs: [...board.jobs, recurringJob, harrieRecurringJob],
            }),
          ),
      );
      const user = userEvent.setup();
      renderApp();

      await screen.findByRole("heading", { name: "Good day, Addie!" });
      const recurringTools = screen.getByText("Routines").closest("details");
      expect(recurringTools).not.toBeNull();
      await user.click(screen.getByText("Routines"));
      await user.click(
        within(recurringTools as HTMLElement).getByRole("checkbox", {
          name: "Harrie",
        }),
      );
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
      await user.click(
        screen.getByRole("button", { name: "Create daily job" }),
      );

      const headings = await screen.findAllByRole("heading", {
        name: "Feed the fish",
      });
      expect(headings).toHaveLength(2);
      const card = headings[0]!.closest("article");
      expect(card).not.toBeNull();
      expect(
        within(card as HTMLElement).getByText("Daily · Morning · 07:30"),
      ).toBeInTheDocument();
      expect(
        await screen.findByText("Daily job created through 2026-10-23."),
      ).toHaveAttribute("role", "status");
    },
  );

  it("creates a take-turns daily job with the chosen first turn", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(board))
      .mockResolvedValueOnce(
        jsonResponse(
          {
            assignments: [
              {
                seriesId: "0d1e7e0e-5f7b-4f4c-9d0f-7a3d3c8a6b21",
                childId: harrie.id,
                generatedThrough: "2026-10-23",
                occurrenceCount: 56,
                rotationChildIds: [harrie.id, fredster.id],
              },
            ],
          },
          { status: 201 },
        ),
      )
      .mockResolvedValueOnce(jsonResponse(board));
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    const recurringTools = screen.getByText("Routines").closest("details");
    expect(recurringTools).not.toBeNull();
    await user.click(screen.getByText("Routines"));
    const tools = within(recurringTools as HTMLElement);
    await user.click(tools.getByRole("checkbox", { name: "Harrie" }));
    expect(tools.queryByLabelText("First turn")).not.toBeInTheDocument();
    await user.click(tools.getByRole("radio", { name: "Take turns" }));
    await user.selectOptions(tools.getByLabelText("First turn"), harrie.id);
    await user.type(screen.getByLabelText("Daily job name"), "Tidy the table");
    await user.click(screen.getByRole("button", { name: "Create daily job" }));

    expect(
      await screen.findByText(
        "Take-turns daily job created through 2026-10-23.",
      ),
    ).toHaveAttribute("role", "status");
    const createRequest = fetch.mock.calls.find(
      ([input]) => requestPath(input) === "/api/recurring-jobs/daily",
    )?.[0];
    expect(createRequest).toBeInstanceOf(Request);
    expect(await (createRequest as Request).clone().json()).toMatchObject({
      childIds: [harrie.id, fredster.id],
      assignmentMode: "takeTurns",
      name: "Tidy the table",
    });
  });

  it.each(["native", "fallback"])(
    "creates a weekly recurring job for selected weekdays (%s UUID)",
    async (mode) => {
      if (mode === "fallback") {
        vi.stubGlobal("crypto", {
          getRandomValues: crypto.getRandomValues.bind(crypto),
        });
      }
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
                assignments: [
                  {
                    seriesId: recurringJob.recurringJobSeriesId,
                    childId: fredster.id,
                    generatedThrough: "2026-10-23",
                    occurrenceCount: 16,
                  },
                ],
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
      await user.click(
        screen.getByRole("button", { name: "Create weekly job" }),
      );

      const heading = await screen.findByRole("heading", {
        name: "Pack sports kit",
      });
      const card = heading.closest("article");
      expect(card).not.toBeNull();
      expect(
        within(card as HTMLElement).getByText("Weekly · Evening · 18:15"),
      ).toBeInTheDocument();
      expect(
        await screen.findByText("Weekly job created through 2026-10-23."),
      ).toHaveAttribute("role", "status");
    },
  );

  it.each(["native", "fallback"])(
    "creates a monthly recurring job for a calendar day (%s UUID)",
    async (mode) => {
      if (mode === "fallback") {
        vi.stubGlobal("crypto", {
          getRandomValues: crypto.getRandomValues.bind(crypto),
        });
      }
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
                assignments: [
                  {
                    seriesId: recurringJob.recurringJobSeriesId,
                    childId: fredster.id,
                    generatedThrough: "2026-10-23",
                    occurrenceCount: 2,
                  },
                ],
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
        await screen.findByText("Monthly job created through 2026-10-23."),
      ).toHaveAttribute("role", "status");
    },
  );

  it("keeps recurring details and sends no request when random generation fails", async () => {
    const fetch = vi.fn().mockResolvedValue(jsonResponse(board));
    vi.stubGlobal("fetch", fetch);
    vi.stubGlobal("crypto", {});
    const user = userEvent.setup();
    renderApp();
    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Routines"));
    const name = screen.getByLabelText("Daily job name");
    await user.type(name, "Feed the fish");
    await user.click(screen.getByRole("button", { name: "Create daily job" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Your browser couldn't prepare this recurring job",
    );
    expect(name).toHaveValue("Feed the fish");
    expect(
      fetch.mock.calls.every(([request]) => request.method === "GET"),
    ).toBe(true);
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
          source: "job",
          name: pendingJob.name,
          jobId: pendingJob.id,
          points: 5,
          awardedAtUtc: "2026-08-29T10:30:00Z",
          loggedByDisplayName: null,
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
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValueOnce(jsonResponse(board))
        .mockResolvedValueOnce(jsonResponse(rejectedJob))
        .mockResolvedValueOnce(jsonResponse(rejectedBoard)),
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
      await within(card as HTMLElement).findByText("Ready for Fredster"),
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

  it("shows the authenticated profile without persisting identity", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(jsonResponse(board)));
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    expect(
      screen.getByText("Addie", { selector: "summary" }),
    ).toBeInTheDocument();
    expect(window.localStorage).toHaveLength(0);
  });

  it("requires deliberate confirmation before resetting task and points data", async () => {
    let reset = false;
    const fetch = vi.fn(
      async (input: RequestInfo | URL, init?: RequestInit) => {
        const request = input instanceof Request ? input : null;
        const path = requestPath(input);
        const method = request?.method ?? init?.method ?? "GET";
        if (path === "/api/admin/jobs-and-points/reset" && method === "POST") {
          expect(await request?.clone().json()).toEqual({
            confirmation: "RESET TASKS AND POINTS",
          });
          reset = true;
          return jsonResponse({
            resetId: "64e3c39d-4d90-4ef5-b561-5f1283841b70",
            occurredAtUtc: "2026-09-19T13:30:00Z",
            deletedJobCount: 3,
            deletedRecurringSeriesCount: 1,
            deletedReviewDecisionCount: 2,
            deletedPointsEntryCount: 1,
          });
        }
        if (path === "/api/today") {
          return jsonResponse(
            reset ? { ...board, jobs: [], pendingApprovalCount: 0 } : board,
          );
        }
        return jsonResponse({}, { status: 404 });
      },
    );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    const resetTools = screen.getByText("Reset data").closest("details");
    expect(resetTools).not.toBeNull();
    await user.click(within(resetTools as HTMLElement).getByText("Reset data"));
    await user.click(
      within(resetTools as HTMLElement).getByRole("button", {
        name: "Reset task and points data",
      }),
    );
    expect(
      within(resetTools as HTMLElement).getByRole("heading", {
        name: "This cannot be undone",
      }),
    ).toBeVisible();
    const finalReset = within(resetTools as HTMLElement).getByRole("button", {
      name: "Permanently reset data",
    });
    expect(finalReset).toBeDisabled();

    await user.click(
      within(resetTools as HTMLElement).getByRole("button", { name: "Cancel" }),
    );
    expect(
      fetch.mock.calls.some(
        ([input]) => requestPath(input) === "/api/admin/jobs-and-points/reset",
      ),
    ).toBe(false);

    await user.click(
      within(resetTools as HTMLElement).getByRole("button", {
        name: "Reset task and points data",
      }),
    );
    const confirmation = within(resetTools as HTMLElement).getByLabelText(
      /Type RESET TASKS AND POINTS to continue/,
    );
    await user.type(confirmation, "RESET TASKS AND POINT");
    expect(
      within(resetTools as HTMLElement).getByRole("button", {
        name: "Permanently reset data",
      }),
    ).toBeDisabled();
    await user.type(confirmation, "S");
    await user.click(
      within(resetTools as HTMLElement).getByRole("button", {
        name: "Permanently reset data",
      }),
    );

    expect(
      await screen.findByText(
        "Task and points data was reset. 3 jobs and 1 point entry were removed.",
      ),
    ).toHaveAttribute("role", "status");
    expect(
      screen.getByText("No jobs are scheduled for this day."),
    ).toBeVisible();
  });

  it("keeps the reset confirmation available when the server fails", async () => {
    const fetch = vi.fn(
      async (input: RequestInfo | URL, init?: RequestInit) => {
        const request = input instanceof Request ? input : null;
        const path = requestPath(input);
        const method = request?.method ?? init?.method ?? "GET";
        if (path === "/api/admin/jobs-and-points/reset" && method === "POST") {
          return jsonResponse(
            {
              detail: "The reset could not be completed. No data was changed.",
            },
            { status: 500 },
          );
        }
        return jsonResponse(board);
      },
    );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    const resetTools = screen.getByText("Reset data").closest("details");
    expect(resetTools).not.toBeNull();
    await user.click(within(resetTools as HTMLElement).getByText("Reset data"));
    await user.click(
      within(resetTools as HTMLElement).getByRole("button", {
        name: "Reset task and points data",
      }),
    );
    const confirmation = within(resetTools as HTMLElement).getByLabelText(
      /Type RESET TASKS AND POINTS to continue/,
    );
    await user.type(confirmation, "RESET TASKS AND POINTS");
    await user.click(
      within(resetTools as HTMLElement).getByRole("button", {
        name: "Permanently reset data",
      }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The reset could not be completed. No data was changed.",
    );
    expect(confirmation).toHaveValue("RESET TASKS AND POINTS");
    expect(
      within(resetTools as HTMLElement).getByRole("heading", {
        name: "This cannot be undone",
      }),
    ).toBeVisible();
  });

  it("loads active family members on demand and shows PIN readiness", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const path = requestPath(input);
        if (path === "/api/users") {
          return jsonResponse([
            managedMember(addie, true, "Avenant"),
            managedMember(fredster, false, "Avenant"),
          ]);
        }
        return jsonResponse(board);
      }),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage family"));

    expect(
      await screen.findByRole("heading", { name: "Family members" }),
    ).toBeInTheDocument();
    expect(await screen.findByText("PIN ready")).toBeInTheDocument();
    expect(screen.getByText("PIN not set")).toBeInTheDocument();
    expect(screen.getByText("No inactive profiles.")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Set up PIN now" }),
    ).toBeInTheDocument();
  });

  it("creates a child and offers a deliberate private PIN handoff", async () => {
    let created = false;
    const fetch = vi.fn(
      async (input: RequestInfo | URL, init?: RequestInit) => {
        const request = input instanceof Request ? input : null;
        const path = requestPath(input);
        const method = request?.method ?? init?.method ?? "GET";
        if (path === "/api/users" && method === "POST") {
          created = true;
          return jsonResponse(managedMember(fredster, false, "Avenant"), {
            status: 201,
          });
        }
        if (path === "/api/users") {
          return jsonResponse([
            managedMember(addie, true, "Avenant"),
            ...(created ? [managedMember(fredster, false, "Avenant")] : []),
          ]);
        }
        return jsonResponse(board);
      },
    );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage family"));
    await screen.findByRole("heading", { name: "Family members" });
    await screen.findByText("PIN ready");
    const addButton = screen.getByRole("button", {
      name: "Add family member",
    });
    const createForm = addButton.closest("form");
    expect(createForm).not.toBeNull();
    const createMember = within(createForm as HTMLFormElement);
    await user.type(createMember.getByLabelText("First name"), "Fred");
    await user.type(createMember.getByLabelText("Surname"), "Avenant");
    await user.type(
      createMember.getByLabelText("Nickname (optional)"),
      "Fredster",
    );
    await user.click(addButton);

    const createdToast = await screen.findByText(/Fredster was added/);
    expect(createdToast).toHaveAttribute("role", "status");
    expect(createdToast.closest(".status-toast")).not.toBeNull();
    expect(
      screen.getByRole("button", { name: "Set up PIN now" }),
    ).toBeInTheDocument();
    const createRequest = fetch.mock.calls.find(([input]) => {
      const request = input instanceof Request ? input : null;
      return requestPath(input) === "/api/users" && request?.method === "POST";
    })?.[0];
    expect(createRequest).toBeInstanceOf(Request);
    expect(await (createRequest as Request).clone().json()).toEqual({
      firstName: "Fred",
      surname: "Avenant",
      nickname: "Fredster",
      role: "child",
    });
    expect(createMember.getByLabelText("First name")).toHaveValue("");
    expect(createMember.getByLabelText("Surname")).toHaveValue("");
    expect(createMember.getByLabelText("Nickname (optional)")).toHaveValue("");
    expect(createMember.getByLabelText("Role")).toHaveValue("child");
  });

  it("keeps family member details after a failed creation", async () => {
    const fetch = vi.fn(
      async (input: RequestInfo | URL, init?: RequestInit) => {
        const request = input instanceof Request ? input : null;
        const path = requestPath(input);
        const method = request?.method ?? init?.method ?? "GET";
        if (path === "/api/users" && method === "POST") {
          return jsonResponse(
            { detail: "That family member already exists." },
            { status: 409 },
          );
        }
        if (path === "/api/users") {
          return jsonResponse([managedMember(addie, true, "Avenant")]);
        }
        return jsonResponse(board);
      },
    );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage family"));
    await screen.findByRole("heading", { name: "Family members" });
    const addButton = await screen.findByRole("button", {
      name: "Add family member",
    });
    const createForm = addButton.closest("form");
    expect(createForm).not.toBeNull();
    const createMember = within(createForm as HTMLFormElement);
    await user.type(createMember.getByLabelText("First name"), "Fred");
    await user.type(createMember.getByLabelText("Surname"), "Avenant");
    await user.type(
      createMember.getByLabelText("Nickname (optional)"),
      "Fredster",
    );
    await user.selectOptions(createMember.getByLabelText("Role"), "adult");
    await user.click(addButton);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "That family member already exists.",
    );
    expect(createMember.getByLabelText("First name")).toHaveValue("Fred");
    expect(createMember.getByLabelText("Surname")).toHaveValue("Avenant");
    expect(createMember.getByLabelText("Nickname (optional)")).toHaveValue(
      "Fredster",
    );
    expect(createMember.getByLabelText("Role")).toHaveValue("adult");
  });

  it("confirms a PIN reset before signing out for the private handoff", async () => {
    const fetch = vi.fn(
      async (input: RequestInfo | URL, init?: RequestInit) => {
        const request = input instanceof Request ? input : null;
        const path = requestPath(input);
        const method = request?.method ?? init?.method ?? "GET";
        if (
          path === `/api/users/${fredster.id}/pin-reset` &&
          method === "POST"
        ) {
          return jsonResponse({
            setupToken: "replacement-one-time-token",
            expiresAtUtc: "2099-01-01T00:05:00Z",
            targetDisplayName: "Fredster",
            targetRole: "child",
          });
        }
        if (path === "/api/users") {
          return jsonResponse([
            managedMember(addie, true, "Avenant"),
            managedMember(fredster, true, "Avenant"),
          ]);
        }
        return jsonResponse(board);
      },
    );
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage family"));
    const familyList = await screen.findByRole("list", {
      name: "Family members",
    });
    const childProfile = within(familyList)
      .getByText("Fredster", { selector: "strong" })
      .closest("li");
    expect(childProfile).not.toBeNull();

    await user.click(
      within(childProfile as HTMLLIElement).getByRole("button", {
        name: "Reset PIN",
      }),
    );
    expect(
      fetch.mock.calls.some(([input]) =>
        requestPath(input).endsWith("pin-reset"),
      ),
    ).toBe(false);
    await user.click(
      within(childProfile as HTMLLIElement).getByRole("button", {
        name: "Keep current PIN",
      }),
    );
    expect(
      within(childProfile as HTMLLIElement).queryByRole("button", {
        name: "Yes, reset and hand over",
      }),
    ).not.toBeInTheDocument();

    await user.click(
      within(childProfile as HTMLLIElement).getByRole("button", {
        name: "Reset PIN",
      }),
    );
    await user.click(
      within(childProfile as HTMLLIElement).getByRole("button", {
        name: "Yes, reset and hand over",
      }),
    );

    expect(
      await screen.findByRole("heading", { name: "Over to Fredster" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/signed out/)).toBeInTheDocument();
    const resetRequest = fetch.mock.calls.find(([input, init]) => {
      const request = input instanceof Request ? input : null;
      return (
        requestPath(input) === `/api/users/${fredster.id}/pin-reset` &&
        (request?.method ?? init?.method) === "POST"
      );
    });
    expect(resetRequest).toBeDefined();
    expect(JSON.parse(String(resetRequest?.[1]?.body))).toEqual({});
  });

  it("keeps an edited profile open and populated when saving fails", async () => {
    const members = [
      managedMember(addie, true, "Avenant"),
      managedMember(fredster, true, "Avenant"),
    ];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const request = input instanceof Request ? input : null;
        const path = requestPath(input);
        if (
          path === `/api/users/${fredster.id}` &&
          request?.method === "PATCH"
        ) {
          return jsonResponse(
            { detail: "That profile couldn't be updated." },
            { status: 409 },
          );
        }
        if (path === "/api/users") {
          return jsonResponse(members);
        }
        return jsonResponse(board);
      }),
    );
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage family"));
    const profile = within(
      await screen.findByRole("list", { name: "Family members" }),
    )
      .getByText("Fredster")
      .closest("li");
    expect(profile).not.toBeNull();
    const editProfile = within(profile as HTMLLIElement);
    await user.click(editProfile.getByText("Edit profile"));
    await user.clear(editProfile.getByLabelText("First name"));
    await user.type(editProfile.getByLabelText("First name"), "Frederick");
    await user.click(editProfile.getByRole("button", { name: "Save profile" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "That profile couldn't be updated.",
    );
    expect(
      editProfile.getByText("Edit profile").closest("details"),
    ).toHaveAttribute("open");
    expect(editProfile.getByLabelText("First name")).toHaveValue("Frederick");
    expect(screen.queryByText(/profile was updated/)).not.toBeInTheDocument();
  });

  it("edits, deliberately deactivates, and restores a family profile", async () => {
    let members = [
      managedMember(addie, true, "Avenant"),
      managedMember(fredster, true, "Avenant"),
    ];
    const fetch = vi.fn(async (input: RequestInfo | URL) => {
      const request = input instanceof Request ? input : null;
      const path = requestPath(input);
      const method = request?.method ?? "GET";
      if (path === `/api/users/${fredster.id}` && method === "PATCH") {
        const names = (await request!.clone().json()) as {
          firstName: string;
          surname: string;
          nickname: string | null;
        };
        members = members.map((member) =>
          member.id === fredster.id
            ? {
                ...member,
                ...names,
                displayName: names.nickname ?? names.firstName,
              }
            : member,
        );
        return jsonResponse(members[1]);
      }
      if (path === `/api/users/${fredster.id}` && method === "DELETE") {
        members = members.map((member) =>
          member.id === fredster.id ? { ...member, isActive: false } : member,
        );
        return jsonResponse(members[1]);
      }
      if (path === `/api/users/${fredster.id}/restore` && method === "POST") {
        members = members.map((member) =>
          member.id === fredster.id ? { ...member, isActive: true } : member,
        );
        return jsonResponse(members[1]);
      }
      if (path === "/api/users") {
        return jsonResponse(members);
      }
      return jsonResponse(board);
    });
    vi.stubGlobal("fetch", fetch);
    const user = userEvent.setup();
    renderApp();

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    await user.click(screen.getByText("Manage family"));
    const familyList = await screen.findByRole("list", {
      name: "Family members",
    });
    const activeProfiles = within(familyList).getAllByRole("listitem");
    const [selfProfile, fredsterProfile] = activeProfiles;
    if (!selfProfile || !fredsterProfile) {
      throw new Error("Expected both active family profiles.");
    }
    expect(within(selfProfile).getByText("Signed-in profile")).toBeVisible();
    expect(
      within(selfProfile).queryByRole("button", {
        name: "Deactivate profile",
      }),
    ).not.toBeInTheDocument();

    await user.click(within(fredsterProfile).getByText("Edit profile"));
    await user.clear(within(fredsterProfile).getByLabelText("First name"));
    await user.type(
      within(fredsterProfile).getByLabelText("First name"),
      "Frederick",
    );
    await user.clear(
      within(fredsterProfile).getByLabelText("Nickname (optional)"),
    );
    await user.type(
      within(fredsterProfile).getByLabelText("Nickname (optional)"),
      "Freddie",
    );
    await user.click(
      within(fredsterProfile).getByRole("button", { name: "Save profile" }),
    );

    const updateToast = await screen.findByText(
      "Freddie's profile was updated.",
    );
    expect(updateToast).toHaveAttribute("role", "status");
    expect(updateToast.closest(".status-toast")).not.toBeNull();
    expect(
      within(fredsterProfile).getByText("Edit profile").closest("details"),
    ).not.toHaveAttribute("open");
    const editRequest = fetch.mock.calls.find(([input]) => {
      const request = input instanceof Request ? input : null;
      return (
        requestPath(input) === `/api/users/${fredster.id}` &&
        request?.method === "PATCH"
      );
    })?.[0];
    expect(editRequest).toBeInstanceOf(Request);
    expect(await (editRequest as Request).clone().json()).toEqual({
      firstName: "Frederick",
      surname: "Avenant",
      nickname: "Freddie",
    });

    const updatedProfile = screen.getByText("Freddie").closest("li");
    expect(updatedProfile).not.toBeNull();
    await user.click(
      within(updatedProfile as HTMLLIElement).getByText("Edit profile"),
    );
    expect(
      within(updatedProfile as HTMLLIElement).getByLabelText("First name"),
    ).toHaveValue("Frederick");
    expect(
      within(updatedProfile as HTMLLIElement).getByLabelText(
        "Nickname (optional)",
      ),
    ).toHaveValue("Freddie");
    await user.click(
      within(updatedProfile as HTMLLIElement).getByText("Edit profile"),
    );
    await user.click(
      within(updatedProfile as HTMLLIElement).getByRole("button", {
        name: "Deactivate profile",
      }),
    );
    expect(
      fetch.mock.calls.some(([input]) => {
        const request = input instanceof Request ? input : null;
        return request?.method === "DELETE";
      }),
    ).toBe(false);
    await user.click(
      within(updatedProfile as HTMLLIElement).getByRole("button", {
        name: "Yes, deactivate",
      }),
    );

    expect(
      await screen.findByText(/Freddie's profile is inactive/),
    ).toHaveAttribute("role", "status");
    expect(
      screen.queryByText("Freddie's profile was updated."),
    ).not.toBeInTheDocument();
    const inactiveList = screen.getByRole("list", {
      name: "Inactive family members",
    });
    const inactiveProfile = within(inactiveList)
      .getByText("Freddie")
      .closest("li");
    expect(inactiveProfile).not.toBeNull();
    await user.click(
      within(inactiveProfile as HTMLLIElement).getByRole("button", {
        name: "Restore profile",
      }),
    );

    expect(
      await screen.findByText("Freddie's profile was restored."),
    ).toHaveAttribute("role", "status");
    expect(
      within(screen.getByRole("list", { name: "Family members" })).getByText(
        "Freddie",
      ),
    ).toBeVisible();
  });
});

function renderApp() {
  acceptSession({
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id: addie.id, displayName: addie.displayName, role: "adult" },
  });
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

function requestPath(input: RequestInfo | URL) {
  return requestUrl(input).pathname;
}

function requestUrl(input: RequestInfo | URL) {
  const value =
    typeof input === "string"
      ? input
      : "url" in input
        ? input.url
        : input.toString();
  return new URL(value, window.location.origin);
}

function managedMember(
  member: typeof addie,
  isCredentialReady: boolean,
  surname: string | null,
  isActive = true,
): FamilyMember {
  return {
    id: member.id,
    firstName: member.firstName,
    surname,
    nickname: member.nickname,
    displayName: member.displayName,
    role: member.isAdult ? "adult" : "child",
    isCredentialReady,
    isActive,
  };
}
