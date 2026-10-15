import { afterEach, describe, expect, it, vi } from "vitest";
import {
  acceptSession,
  authenticatedFetch,
  clearIdentity,
  keepSessionActiveFor,
} from "./auth";

afterEach(() => {
  clearIdentity();
  vi.unstubAllGlobals();
});

it("preserves JSON request headers and body when adding authentication", async () => {
  acceptSession({
    accessToken: "test-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id: "adult", displayName: "Test", role: "adult" },
  });
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const actual = new Request(input, init);
    expect(actual.headers.get("Content-Type")).toBe("application/json");
    expect(actual.headers.get("Authorization")).toBe("Bearer test-token");
    expect(actual.headers.get("X-Request-Test")).toBe("retained");
    expect(actual.method).toBe("POST");
    expect(await actual.json()).toEqual({ name: "Feed the fish" });
    return new Response(null, { status: 201 });
  });
  vi.stubGlobal("fetch", fetch);
  await authenticatedFetch(
    new Request("http://dashboard.home.arpa/api/recurring-jobs/daily", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Request-Test": "retained",
      },
      body: JSON.stringify({ name: "Feed the fish" }),
    }),
  );
  expect(fetch).toHaveBeenCalledOnce();
});

it("keeps saves alive when the page closes, but not reads", async () => {
  acceptSession({
    accessToken: "test-token",
    accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
    member: { id: "adult", displayName: "Test", role: "adult" },
  });
  const fetch = vi.fn<
    (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
  >(async () => new Response(null, { status: 200 }));
  vi.stubGlobal("fetch", fetch);

  await authenticatedFetch(
    new Request("http://dashboard.home.arpa/api/jobs/1/approve", {
      method: "POST",
    }),
  );
  await authenticatedFetch(new Request("http://dashboard.home.arpa/api/today"));

  expect(fetch.mock.calls[0]?.[1]?.keepalive).toBe(true);
  expect(fetch.mock.calls[1]?.[1]?.keepalive).toBeUndefined();
});

it("shares one refresh between saves sent together, so the session isn't revoked", async () => {
  acceptSession({
    accessToken: "expiring-token",
    accessTokenExpiresAtUtc: new Date(Date.now() + 5_000).toISOString(),
    member: { id: "adult", displayName: "Test", role: "adult" },
  });
  const fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = input instanceof Request ? input.url : String(input);
    return url.endsWith("/api/auth/refresh")
      ? new Response(
          JSON.stringify({
            accessToken: "fresh-token",
            accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
            member: { id: "adult", displayName: "Test", role: "adult" },
          }),
          { headers: { "Content-Type": "application/json" } },
        )
      : new Response(null, { status: 200 });
  });
  vi.stubGlobal("fetch", fetch);

  await Promise.all(
    ["1", "2", "3"].map((id) =>
      authenticatedFetch(
        new Request(`http://dashboard.home.arpa/api/jobs/${id}/approve`, {
          method: "POST",
        }),
      ),
    ),
  );

  const refreshes = fetch.mock.calls.filter(([input]) =>
    String(input).endsWith("/api/auth/refresh"),
  );
  expect(refreshes).toHaveLength(1);
  expect(fetch).toHaveBeenCalledTimes(4);
});

describe("keeping the session alive while actions wait", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  function stubRefresh() {
    const fetch = vi.fn<(input: RequestInfo | URL) => Promise<Response>>(
      async () =>
        new Response(
          JSON.stringify({
            accessToken: "fresh-token",
            accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
            member: { id: "adult", displayName: "Test", role: "adult" },
          }),
          { headers: { "Content-Type": "application/json" } },
        ),
    );
    vi.stubGlobal("fetch", fetch);
    return fetch;
  }

  it("sends nothing when the session will outlast the wait", () => {
    vi.useFakeTimers();
    const fetch = stubRefresh();
    acceptSession({
      accessToken: "token",
      accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
      member: { id: "adult", displayName: "Test", role: "adult" },
    });

    vi.advanceTimersByTime(5 * 60_000);
    keepSessionActiveFor(30_000);

    expect(fetch).not.toHaveBeenCalled();
  });

  it("refreshes once when the wait would run past the inactivity limit", () => {
    vi.useFakeTimers();
    const fetch = stubRefresh();
    acceptSession({
      accessToken: "token",
      accessTokenExpiresAtUtc: "2099-01-01T00:00:00Z",
      member: { id: "adult", displayName: "Test", role: "adult" },
    });

    vi.advanceTimersByTime(8 * 60_000);
    keepSessionActiveFor(120_000);
    keepSessionActiveFor(120_000);

    expect(fetch).toHaveBeenCalledOnce();
    expect(String(fetch.mock.calls[0]?.[0])).toContain("/api/auth/refresh");
  });
});
