import createClient from "openapi-fetch";

import type { paths } from "./schema";
import { authenticatedFetch } from "./auth";

export interface TodayJob {
  id: string;
  childId: string;
  childDisplayName: string;
  name: string;
  description: string;
  points: number;
  scheduledDate: string;
  agendaPeriod: "morning" | "arrivingHome" | "evening" | "unscheduled";
  scheduledTime: string | null;
  recurringJobSeriesId: string | null;
  recurrenceFrequency: "daily" | "weekly" | "monthly" | null;
  status: "open" | "pendingApproval" | "approved" | "cancelled";
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
  latestRejection: JobRejection | null;
  recurrence?: RecurringJobSeriesDetails | null;
}

export interface RecurringJobSeriesDetails {
  seriesId: string;
  version: number;
  frequency: "daily" | "weekly" | "monthly";
  weekdays: string[];
  dayOfMonth: number | null;
  startDate: string;
  endDate: string | null;
  takesTurns: boolean;
}

export type RecurringJobChangeScope = "thisOnly" | "allFuture" | "all";

export interface RecurringJobChangeInput {
  operation: "edit" | "cancel";
  scope: RecurringJobChangeScope;
  reason: string | null;
  expectedSeriesVersion: number;
  name: string;
  description: string;
  points: number;
  scheduledDate: string;
  agendaPeriod: "morning" | "arrivingHome" | "evening" | "unscheduled";
  scheduledTime: string | null;
  frequency: "daily" | "weekly" | "monthly";
  weekdays: string[];
  dayOfMonth: number | null;
  endDate: string | null;
}

export interface RecurringJobScopePreview {
  impact: RecurringJobChangeImpact | null;
  error: string | null;
}

export interface RecurringJobChangePreview {
  seriesVersion: number;
  previews: Record<RecurringJobChangeScope, RecurringJobScopePreview>;
}

export interface RecurringJobChangeImpact {
  updatedCount: number;
  createdCount: number;
  cancelledCount: number;
  approvedSkippedCount: number;
  cancelledSkippedCount: number;
  retrospectivePointIncreaseSkippedCount: number;
  warnings: string[];
}

export interface RecurringJobChangeResult {
  requestId: string;
  seriesId: string;
  seriesVersion: number;
  operation: string;
  scope: string;
  impact: RecurringJobChangeImpact;
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
  currentDate: string;
  selectedChildId: string | null;
  jobs: TodayJob[];
  pointsBalance: number | null;
  pendingApprovalCount: number;
  whoseTurns: WhoseTurn[];
}

export interface WhoseTurn {
  rotationId: string;
  question: string;
  childId: string;
  childDisplayName: string;
}

export interface HouseholdMember {
  id: string;
  firstName: string;
  nickname: string | null;
  displayName: string;
  isAdult: boolean;
}

export interface JobApproval {
  job: TodayJob;
  pointsBalance: number;
}

export interface RecurringJobCreation {
  assignments: {
    seriesId: string;
    childId: string;
    generatedThrough: string;
    occurrenceCount: number;
    rotationChildIds: string[];
  }[];
}

export type RecurringAssignmentMode = "eachChild" | "takeTurns";

export class ApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "ApiError";
  }
}

export async function getToday(
  date?: string,
  childId?: string,
): Promise<TodayBoard> {
  const client = apiClient();
  const { data, error } =
    date || childId
      ? await client.GET("/api/today", {
          params: {
            query: {
              ...(date ? { date } : {}),
              ...(childId ? { childId } : {}),
            },
          },
        })
      : await client.GET("/api/today");
  if (!data) {
    throw new ApiError(problemMessage(error, "We couldn't load today's jobs."));
  }

  const jobs = data.jobs.map(mapJob);

  return {
    viewer: data.viewer,
    members: data.members,
    date: data.date,
    currentDate: data.currentDate,
    selectedChildId: data.selectedChildId,
    jobs,
    pointsBalance:
      data.pointsBalance === null ? null : Number(data.pointsBalance),
    pendingApprovalCount: Number(data.pendingApprovalCount),
    whoseTurns: data.whoseTurns ?? [],
  };
}

export async function previewRecurringJobChange(
  id: string,
  change: RecurringJobChangeInput,
): Promise<RecurringJobChangePreview> {
  const client = apiClient();
  const { data, error } = await client.POST(
    "/api/jobs/{id}/recurring-change/preview",
    { params: { path: { id } }, body: change },
  );
  if (!data) {
    throw new ApiError(
      problemMessage(error, "That recurring change couldn't be previewed."),
    );
  }

  return {
    seriesVersion: Number(data.seriesVersion),
    previews: {
      thisOnly: mapScopePreview(data.thisOnly),
      allFuture: mapScopePreview(data.allFuture),
      all: mapScopePreview(data.all),
    },
  };
}

function mapScopePreview(data: {
  impact: Parameters<typeof mapRecurringImpact>[0] | null;
  error: string | null;
}): RecurringJobScopePreview {
  return {
    impact: data.impact ? mapRecurringImpact(data.impact) : null,
    error: data.error,
  };
}

export async function applyRecurringJobChange(
  id: string,
  requestId: string,
  change: RecurringJobChangeInput,
): Promise<RecurringJobChangeResult> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/jobs/{id}/recurring-change", {
    params: { path: { id } },
    body: { requestId, change },
  });
  if (!data) {
    throw new ApiError(
      problemMessage(error, "That recurring change couldn't be applied."),
    );
  }

  return {
    ...data,
    seriesVersion: Number(data.seriesVersion),
    impact: mapRecurringImpact(data.impact),
  };
}

function mapRecurringImpact(data: {
  updatedCount: number | string;
  createdCount: number | string;
  cancelledCount: number | string;
  approvedSkippedCount: number | string;
  cancelledSkippedCount: number | string;
  retrospectivePointIncreaseSkippedCount: number | string;
  warnings: string[];
}): RecurringJobChangeImpact {
  return {
    updatedCount: Number(data.updatedCount),
    createdCount: Number(data.createdCount),
    cancelledCount: Number(data.cancelledCount),
    approvedSkippedCount: Number(data.approvedSkippedCount),
    cancelledSkippedCount: Number(data.cancelledSkippedCount),
    retrospectivePointIncreaseSkippedCount: Number(
      data.retrospectivePointIncreaseSkippedCount,
    ),
    warnings: data.warnings,
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

export async function updateJob(
  id: string,
  request: {
    name: string;
    description: string;
    points: number;
    scheduledDate: string;
    agendaPeriod: "morning" | "arrivingHome" | "evening" | "unscheduled";
    scheduledTime: string | null;
  },
): Promise<TodayJob> {
  const client = apiClient();
  const { data, error } = await client.PUT("/api/jobs/{id}", {
    params: { path: { id } },
    body: request,
  });
  if (!data) {
    throw new ApiError(problemMessage(error, "That job couldn't be updated."));
  }

  return mapJob(data);
}

export async function cancelJob(
  id: string,
  reason: string | null,
): Promise<TodayJob> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/jobs/{id}/cancel", {
    params: { path: { id } },
    body: { reason },
  });
  if (!data) {
    throw new ApiError(
      problemMessage(error, "That job couldn't be cancelled."),
    );
  }

  return mapJob(data);
}

export async function addJob(request: {
  childIds: string[];
  name: string;
  description: string;
  points: number;
  scheduledDate: string;
  agendaPeriod: "morning" | "arrivingHome" | "evening" | "unscheduled";
  scheduledTime: string | null;
}): Promise<TodayJob[]> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/today/jobs", {
    body: request,
  });
  if (!data) {
    throw new ApiError(problemMessage(error, "That job couldn't be added."));
  }

  return data.jobs.map(mapJob);
}

export async function createDailyRecurringJob(request: {
  requestId: string;
  childIds: string[];
  name: string;
  description: string;
  points: number;
  agendaPeriod: "morning" | "arrivingHome" | "evening" | "unscheduled";
  scheduledTime: string | null;
  startDate: string;
  endDate: string | null;
  assignmentMode: RecurringAssignmentMode;
}): Promise<RecurringJobCreation> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/recurring-jobs/daily", {
    body: request,
  });
  if (!data) {
    throw new ApiError(
      problemMessage(error, "That recurring job couldn't be created."),
    );
  }

  return mapRecurringCreation(data);
}

export async function createWeeklyRecurringJob(request: {
  requestId: string;
  childIds: string[];
  name: string;
  description: string;
  points: number;
  agendaPeriod: "morning" | "arrivingHome" | "evening" | "unscheduled";
  scheduledTime: string | null;
  startDate: string;
  endDate: string | null;
  weekdays: string[];
  assignmentMode: RecurringAssignmentMode;
}): Promise<RecurringJobCreation> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/recurring-jobs/weekly", {
    body: request,
  });
  if (!data) {
    throw new ApiError(
      problemMessage(error, "That recurring job couldn't be created."),
    );
  }

  return mapRecurringCreation(data);
}

export async function createMonthlyRecurringJob(request: {
  requestId: string;
  childIds: string[];
  name: string;
  description: string;
  points: number;
  agendaPeriod: "morning" | "arrivingHome" | "evening" | "unscheduled";
  scheduledTime: string | null;
  startDate: string;
  endDate: string | null;
  dayOfMonth: number;
  assignmentMode: RecurringAssignmentMode;
}): Promise<RecurringJobCreation> {
  const client = apiClient();
  const { data, error } = await client.POST("/api/recurring-jobs/monthly", {
    body: request,
  });
  if (!data) {
    throw new ApiError(
      problemMessage(error, "That recurring job couldn't be created."),
    );
  }

  return mapRecurringCreation(data);
}

function mapRecurringCreation(data: {
  assignments: {
    seriesId: string;
    childId: string;
    generatedThrough: string;
    occurrenceCount: number | string;
    rotationChildIds?: string[];
  }[];
}): RecurringJobCreation {
  return {
    assignments: data.assignments.map((assignment) => ({
      ...assignment,
      occurrenceCount: Number(assignment.occurrenceCount),
      rotationChildIds: assignment.rotationChildIds ?? [],
    })),
  };
}

function apiClient() {
  return createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
}

export function mapJob(job: {
  id: string;
  childId: string;
  childDisplayName: string;
  name: string;
  description: string;
  points: number | string;
  scheduledDate: string;
  agendaPeriod: string;
  scheduledTime: string | null;
  recurringJobSeriesId: string | null;
  recurrenceFrequency: string | null;
  status: string;
  completedAtUtc: string | null;
  approvedAtUtc: string | null;
  latestRejection: {
    decisionId: string;
    reason: string | null;
    rejectedAtUtc: string;
  } | null;
  recurrence?: {
    seriesId: string;
    version: number | string;
    frequency: string;
    weekdays: string[];
    dayOfMonth: number | string | null;
    startDate: string;
    endDate: string | null;
    takesTurns: boolean;
  } | null;
}): TodayJob {
  return {
    ...job,
    points: Number(job.points),
    agendaPeriod:
      job.agendaPeriod === "morning" ||
      job.agendaPeriod === "arrivingHome" ||
      job.agendaPeriod === "evening"
        ? job.agendaPeriod
        : "unscheduled",
    recurrenceFrequency:
      job.recurrenceFrequency === "daily" ||
      job.recurrenceFrequency === "weekly" ||
      job.recurrenceFrequency === "monthly"
        ? job.recurrenceFrequency
        : null,
    status:
      job.status === "pendingApproval" ||
      job.status === "approved" ||
      job.status === "cancelled"
        ? job.status
        : "open",
    recurrence: job.recurrence
      ? {
          ...job.recurrence,
          version: Number(job.recurrence.version),
          frequency:
            job.recurrence.frequency === "weekly" ||
            job.recurrence.frequency === "monthly"
              ? job.recurrence.frequency
              : "daily",
          dayOfMonth:
            job.recurrence.dayOfMonth === null
              ? null
              : Number(job.recurrence.dayOfMonth),
        }
      : null,
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
