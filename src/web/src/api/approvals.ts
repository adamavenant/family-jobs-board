import createClient from "openapi-fetch";

import type { paths } from "./schema";
import { authenticatedFetch } from "./auth";
import { mapJob } from "./today";
import type { TodayJob } from "./today";

export class ApprovalsApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "ApprovalsApiError";
  }
}

/** Every job awaiting a grown-up's review, whatever date it was scheduled for. */
export async function getPendingApprovals(): Promise<TodayJob[]> {
  const client = createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
  const { data } = await client.GET("/api/approvals/pending");
  if (!data) {
    throw new ApprovalsApiError("We couldn't load the jobs awaiting review.");
  }

  return data.jobs.map(mapJob);
}
