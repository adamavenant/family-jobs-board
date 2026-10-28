import { act, fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { expectAnnounced } from "../test/undo";
import { undoQueue } from "./undoQueue";
import { UndoToasts } from "./UndoToasts";

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

function queueApproval(
  commit = vi.fn<() => Promise<unknown>>(async () => undefined),
  job = "Feed the dog",
) {
  act(() => {
    undoQueue.enqueue({
      description: `Approved “${job}” for Sam: +5 points`,
      form: new FormData(),
      commit,
      failure: () => "That job changed.",
    });
  });
  return commit;
}

function setup() {
  return userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
}

/**
 * jsdom only treats the first control reached by Tab as :focus-visible, so
 * let focus count as keyboard focus throughout, as it does in a browser
 * once someone uses the keyboard.
 */
function treatFocusAsKeyboardFocus() {
  const matches = Element.prototype.matches;
  vi.spyOn(Element.prototype, "matches").mockImplementation(function (
    this: Element,
    selectors: string,
  ) {
    return selectors === ":focus-visible"
      ? this === document.activeElement
      : matches.call(this, selectors);
  });
}

function toastFor(job: string): HTMLElement {
  return screen
    .getByText(`Approved “${job}” for Sam: +5 points`)
    .closest(".undo-toast") as HTMLElement;
}

describe("undo toast", () => {
  it("announces the action and counts down the household's undo time", async () => {
    undoQueue.setGracePeriodSeconds(10);
    render(<UndoToasts />);
    const commit = queueApproval();

    const message = screen.getByText(
      "Approved “Feed the dog” for Sam: +5 points",
    );
    expectAnnounced(
      "Approved “Feed the dog” for Sam: +5 points. Undo is available for 10 seconds.",
    );
    const toast = message.closest(".undo-toast") as HTMLElement;
    expect(within(toast).getByText("10s")).toBeInTheDocument();
    expect(
      screen.getByRole("region", { name: "Notifications" }),
    ).toContainElement(toast);

    await act(() => vi.advanceTimersByTimeAsync(4_000));
    expect(within(toast).getByText("6s")).toBeInTheDocument();
    expect(commit).not.toHaveBeenCalled();

    await act(() => vi.advanceTimersByTimeAsync(6_000));
    expect(commit).toHaveBeenCalledOnce();
    expect(
      screen.queryByRole("button", { name: "Undo" }),
    ).not.toBeInTheDocument();
  });

  it("lets keyboard users reach Undo and pauses while the toast has focus", async () => {
    const user = setup();
    render(
      <>
        <button type="button">Last control on the page</button>
        <UndoToasts />
      </>,
    );
    const commit = queueApproval();

    screen.getByRole("button", { name: "Last control on the page" }).focus();
    await user.tab();
    const undo = screen.getByRole("button", { name: "Undo" });
    expect(undo).toHaveFocus();
    expect(undo).toHaveAccessibleDescription(
      "Approved “Feed the dog” for Sam: +5 points",
    );
    expect(screen.getByText("Paused")).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(120_000));
    expect(commit).not.toHaveBeenCalled();

    await user.keyboard("{Enter}");
    expect(screen.queryByRole("button", { name: "Undo" })).toBeNull();
    await act(() => vi.advanceTimersByTimeAsync(120_000));
    expect(commit).not.toHaveBeenCalled();
  });

  it("resumes when focus leaves the toast", async () => {
    const user = setup();
    render(
      <>
        <button type="button">Elsewhere</button>
        <UndoToasts />
      </>,
    );
    const commit = queueApproval();

    screen.getByRole("button", { name: "Undo" }).focus();
    await act(() => vi.advanceTimersByTimeAsync(60_000));
    await user.click(screen.getByRole("button", { name: "Elsewhere" }));
    expect(screen.queryByText("Paused")).not.toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(commit).toHaveBeenCalledOnce();
  });

  it("pauses while the pointer is over the toast", async () => {
    const user = setup();
    render(<UndoToasts />);
    const commit = queueApproval();
    const toast = screen
      .getByText("Approved “Feed the dog” for Sam: +5 points")
      .closest(".undo-toast") as HTMLElement;

    await user.hover(toast);
    expect(within(toast).getByText("Paused")).toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(60_000));
    expect(commit).not.toHaveBeenCalled();

    await user.unhover(toast);
    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(commit).toHaveBeenCalledOnce();
  });

  it("saves straight away from the dismiss control", async () => {
    const user = setup();
    render(<UndoToasts />);
    const commit = queueApproval();

    await user.click(
      screen.getByRole("button", { name: "Dismiss and save now" }),
    );

    expect(commit).toHaveBeenCalledOnce();
    expect(screen.queryByRole("button", { name: "Undo" })).toBeNull();
  });

  it("folds older actions into one row below the newest toast", () => {
    render(<UndoToasts />);
    queueApproval(undefined, "Clear the table");
    queueApproval(undefined, "Feed the dog");
    queueApproval(undefined, "Water the plants");

    expect(screen.getAllByRole("button", { name: "Undo" })).toHaveLength(1);
    expect(
      screen.getByRole("button", { name: "Undo" }),
    ).toHaveAccessibleDescription(
      "Approved “Water the plants” for Sam: +5 points",
    );
    expect(screen.queryByText(/Clear the table|Feed the dog/)).toBeNull();
    const row = screen.getByRole("button", { name: "2 more waiting Show" });
    expect(row).toHaveAttribute("aria-expanded", "false");
    expect(row).toHaveTextContent("2 more waiting · Show");
    expect(screen.getAllByRole("status")).toHaveLength(1);
    expectAnnounced(
      "Approved “Water the plants” for Sam: +5 points. Undo is available for 30 seconds.",
    );
  });

  it("shows every folded action from the row, each with its own Undo", async () => {
    const user = setup();
    render(<UndoToasts />);
    const first = queueApproval(undefined, "Clear the table");
    const second = queueApproval(undefined, "Feed the dog");
    const third = queueApproval(undefined, "Water the plants");

    await user.click(
      screen.getByRole("button", { name: "2 more waiting Show" }),
    );
    const row = screen.getByRole("button", { name: "2 more waiting Hide" });
    expect(row).toHaveAttribute("aria-expanded", "true");
    // Newest first, so keyboard users reach the latest action first.
    expect(
      screen
        .getAllByRole("button", { name: "Undo" })
        .map((undo) => undo.getAttribute("aria-describedby"))
        .map((id) => document.getElementById(id!)?.textContent),
    ).toEqual([
      "Approved “Water the plants” for Sam: +5 points",
      "Approved “Feed the dog” for Sam: +5 points",
      "Approved “Clear the table” for Sam: +5 points",
    ]);

    await user.click(
      within(toastFor("Feed the dog")).getByRole("button", { name: "Undo" }),
    );
    await user.click(
      within(toastFor("Clear the table")).getByRole("button", {
        name: "Dismiss and save now",
      }),
    );
    expect(first).toHaveBeenCalledOnce();
    expect(second).not.toHaveBeenCalled();
    expect(third).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /more waiting/ })).toBeNull();
  });

  it("folds the older actions away again from the row", async () => {
    const user = setup();
    render(<UndoToasts />);
    queueApproval(undefined, "Clear the table");
    queueApproval(undefined, "Feed the dog");

    await user.click(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    );
    expect(screen.getAllByRole("button", { name: "Undo" })).toHaveLength(2);
    await user.click(
      screen.getByRole("button", { name: "1 more waiting Hide" }),
    );

    expect(screen.getAllByRole("button", { name: "Undo" })).toHaveLength(1);
    expect(screen.queryByText(/Clear the table/)).toBeNull();
  });

  it("saves each folded action on its own timer", async () => {
    undoQueue.setGracePeriodSeconds(10);
    render(<UndoToasts />);
    const first = queueApproval(undefined, "Clear the table");
    await act(() => vi.advanceTimersByTimeAsync(3_000));
    const second = queueApproval(undefined, "Feed the dog");
    await act(() => vi.advanceTimersByTimeAsync(3_000));
    const third = queueApproval(undefined, "Water the plants");
    expect(
      screen.getByRole("button", { name: "2 more waiting Show" }),
    ).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(4_000));
    expect(first).toHaveBeenCalledOnce();
    expect(second).not.toHaveBeenCalled();
    expect(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    ).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(3_000));
    expect(second).toHaveBeenCalledOnce();
    expect(third).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: /more waiting/ })).toBeNull();
    expect(within(toastFor("Water the plants")).getByText("3s")).toBeVisible();

    await act(() => vi.advanceTimersByTimeAsync(3_000));
    expect(third).toHaveBeenCalledOnce();
  });

  it("lets keyboard users open the row and reach every Undo, pausing the folded actions", async () => {
    treatFocusAsKeyboardFocus();
    undoQueue.setGracePeriodSeconds(10);
    const user = setup();
    render(
      <>
        <button type="button">Elsewhere</button>
        <UndoToasts />
      </>,
    );
    const older = queueApproval(undefined, "Clear the table");
    await act(() => vi.advanceTimersByTimeAsync(2_000));
    const newest = queueApproval(undefined, "Feed the dog");

    screen.getByRole("button", { name: "Elsewhere" }).focus();
    await user.tab();
    await user.tab();
    await user.tab();
    const row = screen.getByRole("button", { name: "1 more waiting Show" });
    expect(row).toHaveFocus();
    // The row stands for the folded action, so its countdown waits too.
    await act(() => vi.advanceTimersByTimeAsync(5_000));
    expect(older).not.toHaveBeenCalled();

    await user.keyboard("{Enter}");
    expect(row).toHaveAccessibleName("1 more waiting Hide");
    expect(row).toHaveAttribute("aria-expanded", "true");
    expect(
      within(toastFor("Clear the table")).getByText("Paused"),
    ).toBeVisible();
    await user.tab();
    const undo = within(toastFor("Clear the table")).getByRole("button", {
      name: "Undo",
    });
    expect(undo).toHaveFocus();
    expect(undo).toHaveAccessibleDescription(
      "Approved “Clear the table” for Sam: +5 points",
    );
    expect(
      within(toastFor("Clear the table")).getByText("Paused"),
    ).toBeVisible();
    await act(() => vi.advanceTimersByTimeAsync(4_000));
    expect(older).not.toHaveBeenCalled();
    expect(newest).not.toHaveBeenCalled();

    await user.click(screen.getByRole("button", { name: "Elsewhere" }));
    await act(() => vi.advanceTimersByTimeAsync(1_000));
    expect(newest).toHaveBeenCalledOnce();
    expect(older).not.toHaveBeenCalled();
    await act(() => vi.advanceTimersByTimeAsync(7_000));
    expect(older).toHaveBeenCalledOnce();
  });

  it("moves focus to the next toast when the focused row goes", async () => {
    treatFocusAsKeyboardFocus();
    undoQueue.setGracePeriodSeconds(10);
    const user = setup();
    render(
      <>
        <button type="button">Elsewhere</button>
        <UndoToasts />
      </>,
    );
    queueApproval(undefined, "Clear the table");
    await act(() => vi.advanceTimersByTimeAsync(2_000));
    const newest = queueApproval(undefined, "Feed the dog");

    screen.getByRole("button", { name: "Elsewhere" }).focus();
    await user.tab();
    await user.tab();
    await user.tab();
    expect(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    ).toHaveFocus();
    // The newest isn't folded, so it saves and the row has nothing left.
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    expect(newest).toHaveBeenCalledOnce();

    expect(screen.queryByRole("button", { name: /more waiting/ })).toBeNull();
    expect(
      within(toastFor("Clear the table")).getByRole("button", { name: "Undo" }),
    ).toHaveFocus();
  });

  it("pauses the folded actions while the pointer is over the row", async () => {
    undoQueue.setGracePeriodSeconds(10);
    const user = setup();
    render(<UndoToasts />);
    const older = queueApproval(undefined, "Clear the table");
    await act(() => vi.advanceTimersByTimeAsync(2_000));
    const newest = queueApproval(undefined, "Feed the dog");

    await user.hover(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    );
    await act(() => vi.advanceTimersByTimeAsync(5_000));
    await user.click(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    );
    expect(
      within(toastFor("Clear the table")).getByText("Paused"),
    ).toBeVisible();
    expect(within(toastFor("Feed the dog")).getByText("5s")).toBeVisible();

    await user.unhover(
      screen.getByRole("button", { name: "1 more waiting Hide" }),
    );
    expect(within(toastFor("Clear the table")).getByText("8s")).toBeVisible();
    await act(() => vi.advanceTimersByTimeAsync(5_000));
    expect(newest).toHaveBeenCalledOnce();
    expect(older).not.toHaveBeenCalled();
    await act(() => vi.advanceTimersByTimeAsync(3_000));
    expect(older).toHaveBeenCalledOnce();
  });

  it("keeps a folded action paused while its toast still holds it", async () => {
    treatFocusAsKeyboardFocus();
    const user = setup();
    render(<UndoToasts />);
    const older = queueApproval(undefined, "Clear the table");
    queueApproval(undefined, "Feed the dog");

    await user.click(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    );
    // The row has focus and the toast has the pointer; both hold the action.
    await user.hover(toastFor("Clear the table"));
    await user.tab({ shift: true });
    expect(
      within(toastFor("Feed the dog")).getByRole("button", {
        name: "Dismiss and save now",
      }),
    ).toHaveFocus();

    expect(
      within(toastFor("Clear the table")).getByText("Paused"),
    ).toBeVisible();
    await act(() => vi.advanceTimersByTimeAsync(60_000));
    expect(older).not.toHaveBeenCalled();
  });

  it("keeps error toasts in full while older actions are folded", async () => {
    undoQueue.setGracePeriodSeconds(10);
    render(<UndoToasts />);
    queueApproval(
      vi.fn<() => Promise<unknown>>().mockRejectedValue(new Error("conflict")),
      "Clear the table",
    );
    await act(() => vi.advanceTimersByTimeAsync(2_000));
    queueApproval(undefined, "Feed the dog");
    await act(() => vi.advanceTimersByTimeAsync(2_000));
    queueApproval(undefined, "Water the plants");

    await act(() => vi.advanceTimersByTimeAsync(6_000));
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Not saved: Approved “Clear the table” for Sam: +5 points. That job changed.",
    );
    expect(screen.getByRole("button", { name: "Try again" })).toBeVisible();
    expect(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Undo" })).toHaveLength(1);
  });

  it("folds the next batch again once only one action is left", async () => {
    const user = setup();
    render(<UndoToasts />);
    queueApproval(undefined, "Clear the table");
    queueApproval(undefined, "Feed the dog");
    await user.click(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    );

    await user.click(
      within(toastFor("Clear the table")).getByRole("button", { name: "Undo" }),
    );
    expect(screen.queryByRole("button", { name: /more waiting/ })).toBeNull();
    queueApproval(undefined, "Water the plants");

    expect(
      screen.getByRole("button", { name: "1 more waiting Show" }),
    ).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText(/Feed the dog/)).toBeNull();
  });

  it("shows a refused save as an alert with Try again", async () => {
    const user = setup();
    render(<UndoToasts />);
    const commit = vi
      .fn<() => Promise<unknown>>()
      .mockRejectedValueOnce(new Error("conflict"))
      .mockResolvedValueOnce(undefined);
    queueApproval(commit);

    await user.click(
      screen.getByRole("button", { name: "Dismiss and save now" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Not saved: Approved “Feed the dog” for Sam: +5 points. That job changed.",
    );
    await user.click(screen.getByRole("button", { name: "Try again" }));
    expect(commit).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("keeps a status region on the page before any toast appears", () => {
    render(<UndoToasts />);

    expect(screen.getByRole("status")).toBeEmptyDOMElement();
    queueApproval();
    expect(screen.getAllByRole("status")).toHaveLength(1);
  });

  it("moves focus to the next toast when the focused one closes", async () => {
    const user = setup();
    render(<UndoToasts />);
    queueApproval(undefined, "Clear the table");
    queueApproval(undefined, "Feed the dog");
    queueApproval(undefined, "Water the plants");
    await user.click(
      screen.getByRole("button", { name: "2 more waiting Show" }),
    );

    within(toastFor("Feed the dog"))
      .getByRole("button", { name: "Undo" })
      .focus();
    await user.keyboard("{Enter}");

    expect(
      within(toastFor("Water the plants")).getByRole("button", {
        name: "Undo",
      }),
    ).toHaveFocus();
  });

  it("moves focus to a toast that was folded away when the newest closes", async () => {
    const user = setup();
    render(<UndoToasts />);
    queueApproval(undefined, "Clear the table");
    queueApproval(undefined, "Feed the dog");

    screen.getByRole("button", { name: "Undo" }).focus();
    await user.keyboard("{Enter}");

    const undo = within(toastFor("Clear the table")).getByRole("button", {
      name: "Undo",
    });
    expect(undo).toHaveFocus();
    expect(undo).toHaveAccessibleDescription(
      "Approved “Clear the table” for Sam: +5 points",
    );
    expect(screen.queryByRole("button", { name: /more waiting/ })).toBeNull();
  });

  it("returns focus to where it came from when the last toast closes", async () => {
    const user = setup();
    render(
      <>
        <button type="button">Approve something</button>
        <UndoToasts />
      </>,
    );
    queueApproval();

    screen.getByRole("button", { name: "Approve something" }).focus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Undo" })).toHaveFocus();
    await user.keyboard("{Enter}");

    expect(
      screen.getByRole("button", { name: "Approve something" }),
    ).toHaveFocus();
  });

  it("falls back to the main content when focus has nowhere to return", async () => {
    const user = setup();
    render(
      <>
        <main>Board</main>
        <UndoToasts />
      </>,
    );
    queueApproval();

    screen.getByRole("button", { name: "Undo" }).focus();
    await user.keyboard("{Enter}");

    expect(screen.getByRole("main")).toHaveFocus();
  });

  it("doesn't pause for a pointer that was already resting where the toast appears", async () => {
    render(<UndoToasts />);
    const commit = queueApproval();
    const toast = screen
      .getByText("Approved “Feed the dog” for Sam: +5 points")
      .closest(".undo-toast") as HTMLElement;

    fireEvent.pointerEnter(toast);
    expect(within(toast).queryByText("Paused")).toBeNull();
    await act(() => vi.advanceTimersByTimeAsync(30_000));
    expect(commit).toHaveBeenCalledOnce();
  });

  it("describes each dismiss control by its toast", () => {
    render(<UndoToasts />);
    queueApproval();

    expect(
      screen.getByRole("button", { name: "Dismiss and save now" }),
    ).toHaveAccessibleDescription("Approved “Feed the dog” for Sam: +5 points");
  });
});
