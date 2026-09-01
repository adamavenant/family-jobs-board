import createClient from "openapi-fetch";

import type { paths } from "./schema";

export interface TodayJob {
  id: string;
  childId: string;
  childDisplayName: string;
  name: string;
  description: string;
  points: number;
  status: "open" | "pendingApproval" | "approved";
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
  latestRejection: JobRejection | null;
}

export interface JobRejection {
  decisionId: string;
  reason: string | null;
  rejectedAtUtc: string;
}

export interface TodayBoard {
  viewer: HouseholdMember;
  members: HouseholdMember[];
  date: string;
  jobs: TodayJob[];
  pointsBalance: number | null;
  pointEarnings: PointEarning[];
  pendingApprovalCount: number;
}

export interface HouseholdMember {
  id: string;
  firstName: string;
  nickname: string | null;
  displayName: string;
  isAdult: boolean;
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

export async function getToday(memberId: string | null): Promise<TodayBoard> {
  const client = apiClient();
  const { data, error } = await client.GET("/api/today", {
    params: { query: memberId ? { memberId } : {} },
  });
  if (!data) {
    throw new ApiError(problemMessage(error, "We couldn't load today's jobs."));
  }

  return {
    viewer: data.viewer,
    members: data.members,
    date: data.date,
    jobs: data.jobs.map(mapJob),
    pointsBalance:
      data.pointsBalance === null ? null : Number(data.pointsBalance),
    pointEarnings: data.pointEarnings.map((earning) => ({
      ...earning,
      points: Number(earning.points),
    })),
    pendingApprovalCount: Number(data.pendingApprovalCount),
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

export async function rejectJob(
  id: string,
  reason: string | null,
): Promise<TodayJob> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/jobs/{id}/reject", {
    params: { path: { id } },
    body: { reason },
  });
  if (!data) {
    throw new ApiError(problemMessage(error, "That job couldn't be rejected."));
  }

  return mapJob(data);
}

export async function addJob(request: {
  childId: string;
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
  childId: string;
  childDisplayName: string;
  name: string;
  description: string;
  points: number | string;
  status: string;
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
  latestRejection: {
    decisionId: string;
    reason: string | null;
    rejectedAtUtc: string;
  } | null;
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
