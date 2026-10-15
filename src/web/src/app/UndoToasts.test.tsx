import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { undoQueue } from "./undoQueue";
import { UndoToasts } from "./UndoToasts";

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
});

afterEach(() => {
  vi.useRealTimers();
});

function queueApproval(
  commit = vi.fn<() => Promise<unknown>>(async () => undefined),
) {
  act(() => {
    undoQueue.enqueue({
      description: "Approved “Feed the dog” for Sam: +5 points",
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

describe("undo toast", () => {
  it("announces the action and counts down the household's undo time", async () => {
    undoQueue.setGracePeriodSeconds(10);
    render(<UndoToasts />);
    const commit = queueApproval();

    const message = screen.getByText(
      "Approved “Feed the dog” for Sam: +5 points",
    );
    expect(message).toHaveAttribute("role", "status");
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

  it("stacks several actions, each with its own Undo", async () => {
    const user = setup();
    render(<UndoToasts />);
    const first = queueApproval();
    const second = queueApproval();

    const undoButtons = screen.getAllByRole("button", { name: "Undo" });
    expect(undoButtons).toHaveLength(2);
    await user.click(undoButtons[0]!);
    await user.click(
      screen.getByRole("button", { name: "Dismiss and save now" }),
    );

    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledOnce();
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
});
