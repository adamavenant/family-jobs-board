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
