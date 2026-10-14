import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

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
const currentDate = "2026-09-22";

type ObserverCallback = (entries: Partial<IntersectionObserverEntry>[]) => void;
let observed: { callback: ObserverCallback; target: Element | undefined }[];

beforeEach(() => {
  observed = [];
  vi.stubGlobal(
    "IntersectionObserver",
    class {
      private readonly entry: {
        callback: ObserverCallback;
        target: Element | undefined;
      };
      public constructor(callback: ObserverCallback) {
        this.entry = { callback, target: undefined };
        observed.push(this.entry);
      }
      public observe(target: Element) {
        this.entry.target = target;
      }
      public disconnect() {
        observed = observed.filter((item) => item !== this.entry);
      }
    },
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  clearIdentity();
  window.localStorage.clear();
});

describe("Floating date bar", () => {
  it("pins the viewed date, day navigation, and review badge once the header date scrolls away", async () => {
    stubApi(addie, { date: "2026-09-23", selectedChildId: fredster.id });
    renderBoard(addie, `/?date=2026-09-23&childId=${fredster.id}`);
    await screen.findByRole("heading", { name: "Good day, Addie!" });
    expect(
      screen.queryByRole("navigation", { name: "Pinned day" }),
    ).not.toBeInTheDocument();
    expect(observed[0]?.target).toHaveClass("hero__date");

    scrollDateLine({ isIntersecting: false, top: -40 });

    const bar = screen.getByRole("navigation", { name: "Pinned day" });
    expect(within(bar).getByText("Wed, Sep 23")).toHaveAttribute(
      "datetime",
      "2026-09-23",
    );
    expect(within(bar).queryByText("Today")).not.toBeInTheDocument();
    expect(
      within(bar).getByRole("link", { name: "Previous day" }),
    ).toHaveAttribute("href", `/?childId=${fredster.id}`);
    expect(within(bar).getByRole("link", { name: "Next day" })).toHaveAttribute(
      "href",
      `/?date=2026-09-24&childId=${fredster.id}`,
    );
    expect(
      within(bar).getByRole("button", { name: "2 jobs awaiting review" }),
    ).toBeInTheDocument();
    expect(bar.querySelector("svg")).toHaveAttribute("aria-hidden", "true");

    scrollDateLine({ isIntersecting: true, top: 120 });

    expect(
      screen.queryByRole("navigation", { name: "Pinned day" }),
    ).not.toBeInTheDocument();
  });

  it("stays hidden while the header date is still below the viewport", async () => {
    stubApi(addie);
    renderBoard(addie, "/");
    await screen.findByRole("heading", { name: "Good day, Addie!" });

    scrollDateLine({ isIntersecting: false, top: 900 });

    expect(
      screen.queryByRole("navigation", { name: "Pinned day" }),
    ).not.toBeInTheDocument();
  });

  it("opens the same review panel as the header badge, which survives scrolling back up", async () => {
    stubApi(addie);
    const user = userEvent.setup();
    renderBoard(addie, "/");
    await screen.findByRole("heading", { name: "Good day, Addie!" });
    scrollDateLine({ isIntersecting: false, top: -40 });

    await user.click(
      within(screen.getByRole("navigation", { name: "Pinned day" })).getByRole(
        "button",
        { name: "2 jobs awaiting review" },
      ),
    );
    expect(
      await screen.findByRole("complementary", { name: "Awaiting review" }),
    ).toBeInTheDocument();
    for (const trigger of screen.getAllByRole("button", {
      name: "2 jobs awaiting review",
    })) {
      expect(trigger).toHaveAttribute("aria-expanded", "true");
    }

    scrollDateLine({ isIntersecting: true, top: 120 });
    expect(
      screen.getByRole("complementary", { name: "Awaiting review" }),
    ).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(
      screen.getByRole("button", { name: "2 jobs awaiting review" }),
    ).toHaveFocus();
  });

  it("shows children the date and Today without a review badge", async () => {
    stubApi(fredster);
    renderBoard(fredster, "/");
    await screen.findByRole("heading", { name: "Good day, Fredster!" });

    scrollDateLine({ isIntersecting: false, top: -40 });

    const bar = screen.getByRole("navigation", { name: "Pinned day" });
    expect(within(bar).getByText("Tue, Sep 22")).toBeInTheDocument();
    expect(within(bar).getByText("Today")).toBeInTheDocument();
    expect(within(bar).queryByRole("button")).not.toBeInTheDocument();
  });
});

function scrollDateLine({
  isIntersecting,
  top,
}: {
  isIntersecting: boolean;
  top: number;
}) {
  act(() => {
    for (const { callback } of observed) {
      callback([
        {
          isIntersecting,
          boundingClientRect: { top } as DOMRectReadOnly,
        },
      ]);
    }
  });
}

function stubApi(
  viewer: typeof addie,
  options: { date?: string; selectedChildId?: string } = {},
) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL((input as Request).url, "http://localhost");
      if (url.pathname === "/api/approvals/pending") {
        return jsonResponse({ jobs: [] });
      }
      return jsonResponse({
        viewer,
        members: [addie, fredster],
        date: options.date ?? currentDate,
        currentDate,
        selectedChildId: options.selectedChildId ?? null,
        jobs: [],
        pointsBalance: viewer.isAdult ? null : 0,
        pendingApprovalCount: 2,
        whoseTurns: [],
      });
    }),
  );
}

function renderBoard(viewer: typeof addie, path: string) {
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

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    headers: { "Content-Type": "application/json" },
  });
}
