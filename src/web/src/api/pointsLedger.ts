import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

type GeneratedLedger = components["schemas"]["PointsLedgerResponse"];
type GeneratedLedgerChild = components["schemas"]["PointsLedgerChildResponse"];
type GeneratedLedgerEntry = components["schemas"]["PointsLedgerEntryResponse"];

// The UI model is the generated contract with its whole-number fields, which
// the contract allows as numeric strings, normalized to numbers.
export type PointsLedgerChild = Omit<GeneratedLedgerChild, "balance"> & {
  balance: number;
};

export type PointsLedgerEntry = Omit<
  GeneratedLedgerEntry,
  "points" | "balanceAfter"
> & {
  points: number;
  balanceAfter: number;
};

export type PointsLedger = Omit<GeneratedLedger, "children" | "entries"> & {
  children: PointsLedgerChild[];
  entries: PointsLedgerEntry[];
};

export class PointsLedgerApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "PointsLedgerApiError";
  }
}

export async function getPointsLedger(options: {
  childId?: string | undefined;
  before?: string | undefined;
}): Promise<PointsLedger> {
  const client = createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
  const { data, error, response } = await client.GET("/api/points-ledger", {
    params: {
      query: {
        ...(options.childId ? { childId: options.childId } : {}),
        ...(options.before ? { before: options.before } : {}),
      },
    },
  });
  if (!data) {
    throw new PointsLedgerApiError(
      response.status === 403
        ? "You can only see your own points."
        : problemMessage(error, "We couldn't load the points."),
    );
  }

  return mapLedger(data);
}

function mapLedger(value: GeneratedLedger): PointsLedger {
  return {
    ...value,
    children: value.children.map(mapChild),
    entries: value.entries.map(mapEntry),
  };
}

function mapChild(value: GeneratedLedgerChild): PointsLedgerChild {
  return { ...value, balance: Number(value.balance) };
}

function mapEntry(value: GeneratedLedgerEntry): PointsLedgerEntry {
  return {
    ...value,
    points: Number(value.points),
    balanceAfter: Number(value.balanceAfter),
  };
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object") {
    const errors = "errors" in error ? error.errors : undefined;
    if (errors && typeof errors === "object") {
      for (const messages of Object.values(errors)) {
        if (Array.isArray(messages) && typeof messages[0] === "string") {
          return messages[0];
        }
      }
    }

    const detail = "detail" in error ? error.detail : undefined;
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }
  }

  return fallback;
}
