import createClient from "openapi-fetch";

import type { paths } from "./schema";
import { authenticatedFetch } from "./auth";

export interface PointRedemptionInput {
  requestId: string;
  childId: string;
  points: number;
  reward: string;
}

export interface RecordedPointRedemption {
  points: number;
  reward: string;
  pointsBalance: number;
}

export class PointRedemptionApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "PointRedemptionApiError";
  }
}

export async function redeemPoints(
  input: PointRedemptionInput,
): Promise<RecordedPointRedemption> {
  const client = createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
  const { data, error } = await client.POST("/api/point-redemptions", {
    body: input,
  });
  if (data) {
    return {
      points: Number(data.redemption.points),
      reward: data.redemption.reward,
      pointsBalance: Number(data.pointsBalance),
    };
  }

  throw new PointRedemptionApiError(
    problemMessage(error, "That redemption couldn't be saved."),
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
