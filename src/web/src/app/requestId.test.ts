import { afterEach, describe, expect, it, vi } from "vitest";
import { createRequestId } from "./requestId";

const randomValues = globalThis.crypto.getRandomValues.bind(globalThis.crypto);
afterEach(() => vi.unstubAllGlobals());

describe("createRequestId", () => {
  it("uses the native UUID implementation when available", () => {
    const randomUUID = vi.fn(() => "11111111-1111-4111-8111-111111111111");
    vi.stubGlobal("crypto", { randomUUID });
    expect(createRequestId()).toBe("11111111-1111-4111-8111-111111111111");
    expect(randomUUID).toHaveBeenCalledOnce();
  });

  it("sets UUID version and variant bits while retaining random bytes", () => {
    vi.stubGlobal("crypto", {
      getRandomValues: (bytes: Uint8Array) => bytes.fill(255),
    });
    expect(createRequestId()).toBe("ffffffff-ffff-4fff-bfff-ffffffffffff");
  });

  it("generates distinct UUID v4 values without randomUUID", () => {
    vi.stubGlobal("crypto", { getRandomValues: randomValues });
    const ids = Array.from({ length: 100 }, createRequestId);
    expect(new Set(ids).size).toBe(100);
    for (const id of ids) {
      expect(id).toMatch(
        /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/,
      );
    }
  });

  it("fails explicitly when secure randomness is unavailable", () => {
    vi.stubGlobal("crypto", undefined);
    expect(createRequestId).toThrow("Secure random generation is unavailable");
  });
});
