import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

type GeneratedResetResult = components["schemas"]["ResetJobsAndPointsResponse"];

export interface ResetJobsAndPointsResult {
  resetId: string;
  occurredAtUtc: string;
  deletedJobCount: number;
  deletedRecurringSeriesCount: number;
  deletedReviewDecisionCount: number;
  deletedPointsEntryCount: number;
}

export class AdministrationApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "AdministrationApiError";
  }
}

export async function resetJobsAndPoints(
  confirmation: string,
): Promise<ResetJobsAndPointsResult> {
  const client = createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
  const { data, error } = await client.POST(
    "/api/admin/jobs-and-points/reset",
    { body: { confirmation } },
  );
  if (!data) {
    throw new AdministrationApiError(
      problemMessage(error, "Task and points data couldn't be reset."),
    );
  }

  return mapResetResult(data);
}

function mapResetResult(value: GeneratedResetResult): ResetJobsAndPointsResult {
  return {
    resetId: value.resetId,
    occurredAtUtc: value.occurredAtUtc,
    deletedJobCount: Number(value.deletedJobCount),
    deletedRecurringSeriesCount: Number(value.deletedRecurringSeriesCount),
    deletedReviewDecisionCount: Number(value.deletedReviewDecisionCount),
    deletedPointsEntryCount: Number(value.deletedPointsEntryCount),
  };
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object") {
    const detail = "detail" in error ? error.detail : undefined;
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }

    const errors = "errors" in error ? error.errors : undefined;
    if (errors && typeof errors === "object") {
      const confirmation = Reflect.get(errors, "Confirmation");
      if (
        Array.isArray(confirmation) &&
        typeof confirmation[0] === "string" &&
        confirmation[0].length > 0
      ) {
        return confirmation[0];
      }
    }
  }

  return fallback;
}
