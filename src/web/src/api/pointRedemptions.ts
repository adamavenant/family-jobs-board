import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

type GeneratedRedeemPointsRequest =
  components["schemas"]["RedeemPointsRequest"];

// The generated request, narrowed to what the UI always sends: a whole number of
// points and a reward that has already been checked.
export type PointRedemptionInput = Omit<
  GeneratedRedeemPointsRequest,
  "points" | "reward"
> & {
  points: number;
  reward: string;
};

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
