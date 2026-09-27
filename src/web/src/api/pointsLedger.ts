import createClient from "openapi-fetch";

import type { paths } from "./schema";
import { authenticatedFetch } from "./auth";

export interface PointsLedger {
  selectedChildId: string | null;
  children: PointsLedgerChild[];
  entries: PointsLedgerEntry[];
  nextCursor: string | null;
}

export interface PointsLedgerChild {
  id: string;
  displayName: string;
  isActive: boolean;
  balance: number;
}

export interface PointsLedgerEntry {
  id: string;
  childId: string;
  childDisplayName: string;
  name: string;
  points: number;
  balanceAfter: number;
  awardedAtUtc: string;
}

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

  return {
    selectedChildId: data.selectedChildId,
    children: data.children.map((child) => ({
      id: child.id,
      displayName: child.displayName,
      isActive: child.isActive,
      balance: Number(child.balance),
    })),
    entries: data.entries.map((entry) => ({
      id: entry.id,
      childId: entry.childId,
      childDisplayName: entry.childDisplayName,
      name: entry.name,
      points: Number(entry.points),
      balanceAfter: Number(entry.balanceAfter),
      awardedAtUtc: entry.awardedAtUtc,
    })),
    nextCursor: data.nextCursor,
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
