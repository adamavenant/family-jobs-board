import { afterEach, expect, it, vi } from "vitest";
import { acceptSession, authenticatedFetch, clearIdentity } from "./auth";

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
