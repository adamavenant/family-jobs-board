import createClient from "openapi-fetch";

import type { paths } from "./schema";

export interface TodayJob {
  id: string;
  name: string;
  description: string;
  points: number;
  status: "open" | "pendingApproval";
  completedAtUtc: string | null;
}

export interface TodayBoard {
  child: { id: string; name: string };
  date: string;
  jobs: TodayJob[];
}

export class ApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "ApiError";
  }
}

export async function getToday(): Promise<TodayBoard> {
  const client = apiClient();
  const { data, error } = await client.GET("/api/today");
  if (!data) {
    throw new ApiError(problemMessage(error, "We couldn't load today's jobs."));
  }

  return {
    child: data.child,
    date: data.date,
    jobs: data.jobs.map(mapJob),
  };
}

export async function completeJob(id: string): Promise<TodayJob> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/jobs/{id}/complete", {
    params: { path: { id } },
  });
  if (!data) {
    throw new ApiError(
      problemMessage(error, "That job couldn't be completed."),
    );
  }

  return mapJob(data);
}

function apiClient() {
  return createClient<paths>({
    baseUrl: window.location.origin,
    fetch: globalThis.fetch,
  });
}

function mapJob(job: {
  id: string;
  name: string;
  description: string;
  points: number | string;
  status: string;
  completedAtUtc: string | null;
}): TodayJob {
  return {
    ...job,
    points: Number(job.points),
    status: job.status === "pendingApproval" ? "pendingApproval" : "open",
  };
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object" && "detail" in error) {
    const detail = error.detail;
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }
  }

  return fallback;
}
