import { afterEach, expect, it, vi } from "vitest";
import { RouterContextProvider } from "react-router";
import { routes } from "./routes";
import { createDailyRecurringJob } from "../api/today";

vi.mock("../api/today", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/today")>()),
  createDailyRecurringJob: vi.fn(),
}));

afterEach(() => {
  vi.unstubAllGlobals();
  vi.resetAllMocks();
});

it("preserves an explicitly supplied request ID without generating another", async () => {
  vi.stubGlobal("crypto", {});
  vi.mocked(createDailyRecurringJob).mockResolvedValue({
    assignments: [
      {
        seriesId: "series",
        childId: "child",
        generatedThrough: "2026-11-01",
        occurrenceCount: 56,
      },
    ],
  });
  const form = new FormData();
  for (const [key, value] of Object.entries({
    intent: "addRecurring",
    requestId: "11111111-1111-4111-8111-111111111111",
    recurrenceFrequency: "daily",
    childIds: "child",
    name: "Feed the fish",
    description: "",
    points: "3",
    agendaPeriod: "morning",
    startDate: "2026-09-14",
  }))
    form.set(key, value);
  const action = routes[0]?.action;
  if (typeof action !== "function") throw new Error("Missing route action");
  await action({
    request: new Request("http://dashboard.home.arpa", {
      method: "POST",
      body: form,
    }),
    params: {},
    url: new URL("http://dashboard.home.arpa/"),
    pattern: "/",
    context: new RouterContextProvider(),
  });
  expect(createDailyRecurringJob).toHaveBeenCalledWith(
    expect.objectContaining({
      requestId: "11111111-1111-4111-8111-111111111111",
    }),
  );
});
