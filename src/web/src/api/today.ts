import createClient from "openapi-fetch";

import type { paths } from "./schema";

export interface TodayJob {
  id: string;
  name: string;
  description: string;
  points: number;
  status: "open" | "pendingApproval" | "approved";
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
}

export interface TodayBoard {
  child: {
    id: string;
    firstName: string;
    nickname: string | null;
    displayName: string;
    pointsBalance: number;
  };
  date: string;
  jobs: TodayJob[];
  pointEarnings: PointEarning[];
}

export interface PointEarning {
  id: string;
  jobId: string;
  jobName: string;
  points: number;
  awardedAtUtc: string;
}

export interface JobApproval {
  job: TodayJob;
  pointsBalance: number;
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
    child: {
      ...data.child,
      pointsBalance: Number(data.child.pointsBalance),
    },
    date: data.date,
    jobs: data.jobs.map(mapJob),
    pointEarnings: data.pointEarnings.map((earning) => ({
      ...earning,
      points: Number(earning.points),
    })),
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

export async function approveJob(id: string): Promise<JobApproval> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/jobs/{id}/approve", {
    params: { path: { id } },
  });
  if (!data) {
    throw new ApiError(problemMessage(error, "That job couldn't be approved."));
  }

  return {
    job: mapJob(data.job),
    pointsBalance: Number(data.pointsBalance),
  };
}

export async function addJob(request: {
  name: string;
  description: string;
  points: number;
}): Promise<TodayJob> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/today/jobs", {
    body: request,
  });
  if (!data) {
    throw new ApiError(problemMessage(error, "That job couldn't be added."));
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
  approvedAtUtc: string | null;
}): TodayJob {
  return {
    ...job,
    points: Number(job.points),
    status:
      job.status === "pendingApproval" || job.status === "approved"
        ? job.status
        : "open",
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
