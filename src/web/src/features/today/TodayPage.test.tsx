import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { routes } from "../../app/routes";

const board = {
  child: { id: "c7b3309f-c84c-4b90-b923-305597484642", name: "Alex" },
  date: "2026-08-29",
  jobs: [
    {
      id: "7009b529-733c-4770-ae56-1f6fa69f6363",
      name: "Feed the dog",
      description: "Fill the food bowl and make sure there is fresh water.",
      points: 5,
      status: "open",
      completedAtUtc: null,
    },
    {
      id: "b9d6a90c-58e4-4606-bf65-61de33c2573d",
      name: "Pack school bag",
      description: "Check tomorrow's timetable and pack everything needed.",
      points: 8,
      status: "open",
      completedAtUtc: null,
    },
    {
      id: "ea64b5d3-ab18-4c75-bc33-eb3cbf7524f6",
      name: "Clear the table",
      description: "Take dishes to the kitchen after dinner.",
      points: 5,
      status: "pendingApproval",
      completedAtUtc: "2026-08-29T09:00:00Z",
    },
  ],
};

afterEach(() => vi.unstubAllGlobals());

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
      screen.getByText("Nice work — sent for approval"),
    ).toBeInTheDocument();
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
