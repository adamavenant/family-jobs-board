import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

type GeneratedPointAdjustmentRequest =
  components["schemas"]["RecordPointAdjustmentRequest"];

// The generated request, narrowed to what the UI always sends: a whole number of
// points and a reason that has already been checked.
export type PointAdjustmentInput = Omit<
  GeneratedPointAdjustmentRequest,
  "amount" | "reason"
> & {
  amount: number;
  reason: string;
};

export interface RecordedPointAdjustment {
  amount: number;
  pointsBalance: number;
}

export class PointAdjustmentApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "PointAdjustmentApiError";
  }
}

export async function recordPointAdjustment(
  input: PointAdjustmentInput,
): Promise<RecordedPointAdjustment> {
  const client = createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
  const { data, error } = await client.POST("/api/point-adjustments", {
    body: input,
  });
  if (data) {
    return {
      amount: Number(data.adjustment.amount),
      pointsBalance: Number(data.pointsBalance),
    };
  }

  throw new PointAdjustmentApiError(
    problemMessage(error, "That points adjustment couldn't be saved."),
  );
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object") {
    const errors = Reflect.get(error, "errors");
    if (errors && typeof errors === "object") {
      for (const messages of Object.values(errors)) {
        if (Array.isArray(messages) && typeof messages[0] === "string") {
          return messages[0];
        }
      }
    }

    const detail = Reflect.get(error, "detail");
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }
  }

  return fallback;
}
