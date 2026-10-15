import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { createUndoQueue } from "./undoQueue";
import type { UndoQueue } from "./undoQueue";

let queue: UndoQueue;
let revalidate: ReturnType<typeof vi.fn<() => Promise<void>>>;

beforeEach(() => {
  vi.useFakeTimers();
  queue = createUndoQueue();
  revalidate = vi.fn<() => Promise<void>>(async () => undefined);
  queue.connect(revalidate);
});

afterEach(() => {
  queue.reset();
  vi.useRealTimers();
});

function enqueue(
  commit: () => Promise<unknown> = vi.fn(async () => undefined),
  description = "Approved “Feed the dog” for Sam",
) {
  const form = new FormData();
  form.set("intent", "approve");
  return {
    commit,
    id: queue.enqueue({
      description,
      form,
      commit,
      failure: (error) =>
        error instanceof Error ? error.message : "It couldn't be saved.",
    }),
  };
}

describe("undo queue", () => {
  it("sends nothing until the undo time ends, then saves once and revalidates", async () => {
    const { commit } = enqueue();

    await vi.advanceTimersByTimeAsync(29_999);
    expect(commit).not.toHaveBeenCalled();
    expect(queue.getSnapshot().actions[0]?.status).toBe("waiting");

    await vi.advanceTimersByTimeAsync(1);
    expect(commit).toHaveBeenCalledOnce();
    expect(revalidate).toHaveBeenCalledOnce();
    expect(queue.getSnapshot().actions).toHaveLength(0);

    await vi.advanceTimersByTimeAsync(60_000);
    expect(commit).toHaveBeenCalledOnce();
  });

  it("keeps the item queued as saving until the page has revalidated", async () => {
    let finishRevalidation = () => {};
    revalidate.mockImplementation(
      () => new Promise<void>((resolve) => (finishRevalidation = resolve)),
    );
    enqueue();

    await vi.advanceTimersByTimeAsync(30_000);
    expect(queue.getSnapshot().actions[0]?.status).toBe("saving");

    finishRevalidation();
    await vi.waitFor(() => expect(queue.getSnapshot().actions).toHaveLength(0));
  });

  it("drops an undone action without sending or revalidating", async () => {
    const { commit, id } = enqueue();

    await vi.advanceTimersByTimeAsync(10_000);
    queue.undo(id);
    await vi.advanceTimersByTimeAsync(60_000);

    expect(commit).not.toHaveBeenCalled();
    expect(revalidate).not.toHaveBeenCalled();
    expect(queue.getSnapshot().actions).toHaveLength(0);
  });

  it("saves straight away when dismissed and never again when the timer would have ended", async () => {
    const { commit, id } = enqueue();

    queue.saveNow(id);
    queue.saveNow(id);
    await vi.advanceTimersByTimeAsync(60_000);

    expect(commit).toHaveBeenCalledOnce();
    expect(revalidate).toHaveBeenCalledOnce();
  });

  it("ignores Undo once saving has started", async () => {
    let finishCommit = () => {};
    const commit = vi.fn(
      () => new Promise<void>((resolve) => (finishCommit = resolve)),
    );
    const { id } = enqueue(commit);

    await vi.advanceTimersByTimeAsync(30_000);
    queue.undo(id);
    finishCommit();
    await vi.waitFor(() => expect(revalidate).toHaveBeenCalledOnce());

    expect(commit).toHaveBeenCalledOnce();
  });

  it("pauses the countdown and resumes with the time that was left", async () => {
    const { commit, id } = enqueue();

    await vi.advanceTimersByTimeAsync(20_000);
    queue.pause(id);
    expect(queue.getSnapshot().actions[0]).toMatchObject({
      remainingMs: 10_000,
      runningSince: null,
    });
    await vi.advanceTimersByTimeAsync(120_000);
    expect(commit).not.toHaveBeenCalled();

    queue.resume(id);
    await vi.advanceTimersByTimeAsync(9_999);
    expect(commit).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(commit).toHaveBeenCalledOnce();
  });

  it("saves several queued actions independently", async () => {
    const first = enqueue();
    await vi.advanceTimersByTimeAsync(5_000);
    const second = enqueue();
    const third = enqueue();

    queue.undo(second.id);
    await vi.advanceTimersByTimeAsync(25_000);
    expect(first.commit).toHaveBeenCalledOnce();
    expect(third.commit).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(5_000);
    expect(third.commit).toHaveBeenCalledOnce();
    expect(second.commit).not.toHaveBeenCalled();
  });

  it("explains a refused save, still revalidates, and can send the same request again", async () => {
    const commit = vi
      .fn<() => Promise<unknown>>()
      .mockRejectedValueOnce(new Error("The points changed. Refresh."))
      .mockResolvedValueOnce(undefined);
    enqueue(commit);

    await vi.advanceTimersByTimeAsync(30_000);
    const [notice] = queue.getSnapshot().notices;
    expect(notice).toMatchObject({
      tone: "error",
      canRetry: true,
      message:
        "Not saved: Approved “Feed the dog” for Sam. The points changed. Refresh.",
    });
    expect(revalidate).toHaveBeenCalledOnce();

    queue.retry(notice!.id);
    await vi.waitFor(() => expect(revalidate).toHaveBeenCalledTimes(2));
    expect(commit).toHaveBeenCalledTimes(2);
    expect(queue.getSnapshot().notices).toHaveLength(0);
  });

  it("sends every queued action at once when flushed and waits for them", async () => {
    const order: string[] = [];
    let finishSlow = () => {};
    const slow = enqueue(
      vi.fn(
        () =>
          new Promise<void>((resolve) => {
            finishSlow = () => {
              order.push("slow");
              resolve();
            };
          }),
      ),
    );
    const fast = enqueue(
      vi.fn(async () => {
        order.push("fast");
      }),
    );

    const flushed = queue.flush().then(() => order.push("flushed"));
    expect(slow.commit).toHaveBeenCalledOnce();
    expect(fast.commit).toHaveBeenCalledOnce();
    await vi.advanceTimersByTimeAsync(0);
    expect(order).toEqual(["fast"]);

    finishSlow();
    await flushed;
    expect(order).toEqual(["fast", "slow", "flushed"]);
    await vi.advanceTimersByTimeAsync(60_000);
    expect(slow.commit).toHaveBeenCalledOnce();
    expect(fast.commit).toHaveBeenCalledOnce();
  });

  it("uses the household's undo time for later actions and ignores out-of-range values", async () => {
    queue.setGracePeriodSeconds(45);
    const longer = enqueue();
    for (const invalid of [4, 121, 30.5, Number.NaN, undefined]) {
      queue.setGracePeriodSeconds(invalid);
    }
    const alsoLonger = enqueue();

    await vi.advanceTimersByTimeAsync(44_999);
    expect(longer.commit).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(longer.commit).toHaveBeenCalledOnce();
    expect(alsoLonger.commit).toHaveBeenCalledOnce();

    queue.setGracePeriodSeconds(5);
    const shortest = enqueue();
    await vi.advanceTimersByTimeAsync(5_000);
    expect(shortest.commit).toHaveBeenCalledOnce();
  });

  it("keeps one success message at a time but every error", () => {
    queue.notify("Undo time saved.");
    queue.notify("Data was reset.");

    expect(queue.getSnapshot().notices.map((notice) => notice.message)).toEqual(
      ["Data was reset."],
    );
  });
});
