import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, describe, expect, it, vi } from "vitest";

import { acceptSession, clearIdentity } from "../../api/auth";
import { routes } from "../../app/routes";

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
const harrie = {
  id: "e22facf5-69ce-45ce-9dad-306eef1852c9",
  firstName: "Harrie",
  nickname: null,
  displayName: "Harrie",
  isAdult: false,
};
const currentDate = "2026-09-22";
const yesterday = "2026-09-21";

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
  window.localStorage.clear();
});

describe("Approvals panel", () => {
  it("lists pending work from every date and approves it without leaving the calendar", async () => {
    const server = stubServer([
      job({ id: "old", name: "Feed the dog", scheduledDate: yesterday }),
      job({ id: "new", name: "Pack school bag", child: harrie }),
    ]);
    const user = userEvent.setup();
    renderAt("/calendar?view=month");

    await user.click(
      await screen.findByRole("button", { name: "2 jobs awaiting review" }),
    );
    const panel = await screen.findByRole("complementary", {
      name: "Awaiting review",
    });
    expect(
      within(panel).getByRole("heading", { name: "Awaiting review" }),
    ).toHaveFocus();
    expect(
      await within(panel).findByRole("heading", { name: /^Yesterday/ }),
    ).toBeInTheDocument();
    expect(
      within(panel).getByRole("heading", { name: /^Today/ }),
    ).toBeInTheDocument();

    await user.click(
      within(panel).getByRole("button", {
        name: "Approve +5 Feed the dog for Fredster",
      }),
    );

    await waitFor(() =>
      expect(within(panel).queryByText("Feed the dog")).not.toBeInTheDocument(),
    );
    expect(server.requests).toContainEqual(
      "POST /api/jobs/old/approve?expectedPoints=5",
    );
    expect(
      await screen.findByRole("button", { name: "1 jobs awaiting review" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Calendar" })).toBeVisible();
    expect(within(panel).getByText("Pack school bag")).toBeInTheDocument();
  });

  it("rejects with feedback and keeps the reason when the rejection fails", async () => {
    const server = stubServer([job({ id: "old", name: "Feed the dog" })]);
    server.failRejections = true;
    const user = userEvent.setup();
    renderAt("/calendar?view=month");

    await user.click(
      await screen.findByRole("button", { name: "1 jobs awaiting review" }),
    );
    const panel = await screen.findByRole("complementary", {
      name: "Awaiting review",
    });
    await user.click(await within(panel).findByText("Reject…"));
    await user.type(
      within(panel).getByLabelText(/Rejection reason/),
      "The bowl is still empty",
    );
    await user.click(
      within(panel).getByRole("button", {
        name: "Reject job Feed the dog for Fredster",
      }),
    );

    expect(await within(panel).findByRole("alert")).toHaveTextContent(
      "Job cannot be rejected",
    );
    expect(within(panel).getByLabelText(/Rejection reason/)).toHaveValue(
      "The bowl is still empty",
    );

    server.failRejections = false;
    await user.click(
      within(panel).getByRole("button", {
        name: "Reject job Feed the dog for Fredster",
      }),
    );
    expect(
      await within(panel).findByText(/Nothing is waiting for review/),
    ).toBeInTheDocument();
    expect(server.rejectionReasons).toEqual(["The bowl is still empty"]);
  });

  it("closes with Escape and returns focus to the badge", async () => {
    stubServer([]);
    const user = userEvent.setup();
    renderAt("/calendar?view=month");

    const badge = await screen.findByRole("button", {
      name: "0 jobs awaiting review",
    });
    await user.click(badge);
    expect(badge).toHaveAttribute("aria-expanded", "true");
    await screen.findByText(/Nothing is waiting for review/);

    await user.keyboard("{Escape}");

    expect(
      screen.queryByRole("complementary", { name: "Awaiting review" }),
    ).not.toBeInTheDocument();
    expect(badge).toHaveFocus();
    expect(badge).toHaveAttribute("aria-expanded", "false");
  });

  it("approves a pending job straight from its row in the calendar week view", async () => {
    const server = stubServer([job({ id: "new", name: "Pack school bag" })]);
    const user = userEvent.setup();
    renderAt("/calendar?view=week");

    const row = (await screen.findByRole("link", { name: /Pack school bag/ }))
      .parentElement!;
    await user.click(
      within(row).getByRole("button", {
        name: "Approve +5 Pack school bag for Fredster",
      }),
    );

    expect(
      await screen.findByRole("button", { name: "0 jobs awaiting review" }),
    ).toBeInTheDocument();
    expect(server.requests).toContainEqual(
      "POST /api/jobs/new/approve?expectedPoints=5",
    );
    expect(
      within(row).queryByRole("button", { name: /Approve/ }),
    ).not.toBeInTheDocument();
  });

  it("counts earlier days' pending work on today's board and reviews it from there", async () => {
    stubServer([
      job({ id: "old", name: "Feed the dog", scheduledDate: yesterday }),
    ]);
    const user = userEvent.setup();
    renderAt("/");

    await screen.findByRole("heading", { name: "Good day, Addie!" });
    expect(screen.queryByText("Feed the dog")).not.toBeInTheDocument();
    await user.click(
      screen.getByRole("button", { name: "1 jobs awaiting review" }),
    );

    const panel = await screen.findByRole("complementary", {
      name: "Awaiting review",
    });
    expect(await within(panel).findByText("Feed the dog")).toBeInTheDocument();
  });

  it("shows children their points instead of the review badge", async () => {
    stubServer([job({ id: "old", name: "Feed the dog" })]);
    renderAt("/", fredster);

    await screen.findByRole("heading", { name: "Good day, Fredster!" });
    expect(
      screen.queryByRole("button", { name: /awaiting review/ }),
    ).not.toBeInTheDocument();
  });
});

type Member = typeof addie;

interface RawJob {
  id: string;
  childId: string;
  childDisplayName: string;
  name: string;
  description: string;
  points: number;
  scheduledDate: string;
  agendaPeriod: string;
  scheduledTime: null;
  recurringJobSeriesId: null;
  recurrenceFrequency: null;
  status: string;
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
  latestRejection: null;
}

function job(options: {
  id: string;
  name: string;
  scheduledDate?: string;
  child?: Member;
}): RawJob {
  const child = options.child ?? fredster;
  return {
    id: options.id,
    childId: child.id,
    childDisplayName: child.displayName,
    name: options.name,
    description: "",
    points: 5,
    scheduledDate: options.scheduledDate ?? currentDate,
    agendaPeriod: "morning",
    scheduledTime: null,
    recurringJobSeriesId: null,
    recurrenceFrequency: null,
    status: "pendingApproval",
    completedAtUtc: "2026-09-21T15:00:00Z",
    approvedAtUtc: null,
    latestRejection: null,
  };
}

/** A small in-memory household that answers the API calls the review flows make. */
function stubServer(jobs: RawJob[]) {
  const server = {
    jobs,
    requests: [] as string[],
    rejectionReasons: [] as string[],
    failRejections: false,
  };
  const members = [addie, fredster, harrie];
  const pending = () =>
    server.jobs.filter((item) => item.status === "pendingApproval");

  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      const url = new URL(request.url, "http://localhost");
      server.requests.push(`${request.method} ${url.pathname}${url.search}`);

      const decision = url.pathname.match(
        /^\/api\/jobs\/([^/]+)\/(approve|reject)$/,
      );
      if (decision) {
        const target = server.jobs.find((item) => item.id === decision[1])!;
        if (decision[2] === "reject") {
          if (server.failRejections) {
            return jsonResponse(
              { title: "Conflict", detail: "Job cannot be rejected." },
              { status: 409 },
            );
          }
          const body = (await request.json()) as { reason: string | null };
          server.rejectionReasons.push(body.reason ?? "");
          target.status = "open";
          return jsonResponse(target);
        }
        target.status = "approved";
        return jsonResponse({ job: target, pointsBalance: 5 });
      }
      if (url.pathname === "/api/approvals/pending") {
        return jsonResponse({ jobs: pending() });
      }
      if (url.pathname === "/api/calendar") {
        const view = url.searchParams.get("view") ?? "week";
        const start = view === "week" ? "2026-09-21" : "2026-08-31";
        const length = view === "week" ? 7 : 42;
        return jsonResponse({
          viewer: addie,
          members,
          view,
          anchorDate: currentDate,
          currentDate,
          rangeStart: start,
          rangeEnd: addDays(start, length - 1),
          selectedChildId: null,
          pendingApprovalCount: pending().length,
          days: Array.from({ length }, (_, index) => {
            const date = addDays(start, index);
            return {
              date,
              isInFocusedPeriod: true,
              jobs: server.jobs.filter((item) => item.scheduledDate === date),
            };
          }),
        });
      }
      if (url.pathname === "/api/today") {
        const viewer = currentViewer;
        return jsonResponse({
          viewer,
          members,
          date: currentDate,
          currentDate,
          selectedChildId: null,
          jobs: server.jobs.filter(
            (item) =>
              item.scheduledDate === currentDate &&
              (viewer.isAdult || item.childId === viewer.id),
          ),
          pointsBalance: viewer.isAdult ? null : 0,
          pendingApprovalCount: pending().length,
          whoseTurns: [],
        });
      }

      return jsonResponse({ title: "Not found" }, { status: 404 });
    }),
  );
  return server;
}

let currentViewer: Member = addie;

function renderAt(path: string, viewer: Member = addie) {
  currentViewer = viewer;
  acceptSession({
    accessToken: "test-access-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: {
      id: viewer.id,
      displayName: viewer.displayName,
      role: viewer.isAdult ? "adult" : "child",
    },
  });
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  return render(<RouterProvider router={router} />);
}

function addDays(date: string, days: number): string {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

function jsonResponse(body: unknown, init?: ResponseInit) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/problem+json" },
    ...init,
  });
}
