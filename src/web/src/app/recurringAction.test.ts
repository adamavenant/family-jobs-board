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
        rotationChildIds: [],
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

async function submitRecurring(entries: [string, string][]) {
  const form = new FormData();
  for (const [key, value] of entries) form.append(key, value);
  const action = routes[0]?.action;
  if (typeof action !== "function") throw new Error("Missing route action");
  return action({
    request: new Request("http://dashboard.home.arpa", {
      method: "POST",
      body: form,
    }),
    params: {},
    url: new URL("http://dashboard.home.arpa/"),
    pattern: "/",
    context: new RouterContextProvider(),
  });
}

const takeTurnsFields: [string, string][] = [
  ["intent", "addRecurring"],
  ["requestId", "22222222-2222-4222-8222-222222222222"],
  ["recurrenceFrequency", "daily"],
  ["name", "Tidy the table"],
  ["description", ""],
  ["points", "1"],
  ["agendaPeriod", "evening"],
  ["startDate", "2026-09-14"],
  ["assignmentMode", "takeTurns"],
];

it("rotates take-turns children so the chosen first turn leads", async () => {
  vi.mocked(createDailyRecurringJob).mockResolvedValue({
    assignments: [
      {
        seriesId: "series",
        childId: "b",
        generatedThrough: "2026-11-01",
        occurrenceCount: 56,
        rotationChildIds: ["b", "c", "a"],
      },
    ],
  });

  const result = await submitRecurring([
    ...takeTurnsFields,
    ["childIds", "a"],
    ["childIds", "b"],
    ["childIds", "c"],
    ["firstTurnChildId", "b"],
  ]);

  expect(createDailyRecurringJob).toHaveBeenCalledWith(
    expect.objectContaining({
      childIds: ["b", "c", "a"],
      assignmentMode: "takeTurns",
    }),
  );
  expect(result).toEqual(
    expect.objectContaining({ success: true, takesTurns: true }),
  );
});

it("refuses to take turns with fewer than two children", async () => {
  const result = await submitRecurring([...takeTurnsFields, ["childIds", "a"]]);

  expect(createDailyRecurringJob).not.toHaveBeenCalled();
  expect(result).toEqual(
    expect.objectContaining({
      error: "Choose at least two children to take turns.",
    }),
  );
});

it("refuses a first turn that is not one of the selected children", async () => {
  const result = await submitRecurring([
    ...takeTurnsFields,
    ["childIds", "a"],
    ["childIds", "b"],
    ["firstTurnChildId", "c"],
  ]);

  expect(createDailyRecurringJob).not.toHaveBeenCalled();
  expect(result).toEqual(
    expect.objectContaining({
      error: "Choose a first turn from the selected children.",
    }),
  );
});
