import type { ActionFunctionArgs, LoaderFunctionArgs } from "react-router";
import type { RouteObject } from "react-router";

import {
  ApiError,
  addJob,
  approveJob,
  completeJob,
  createDailyRecurringJob,
  createMonthlyRecurringJob,
  createWeeklyRecurringJob,
  getToday,
  rejectJob,
} from "../api/today";
import { LoadingPage } from "./LoadingPage";
import { TodayPage } from "../features/today/TodayPage";

export interface CompleteActionResult {
  intent: "complete";
  jobId: string;
  error?: string;
}

export interface AddJobActionResult {
  intent: "add";
  success?: boolean;
  error?: string;
}

export interface ApproveActionResult {
  intent: "approve";
  jobId: string;
  error?: string;
}

export interface AddRecurringJobActionResult {
  intent: "addRecurring";
  frequency?: "daily" | "weekly" | "monthly";
  success?: boolean;
  generatedThrough?: string;
  error?: string;
}

export interface RejectActionResult {
  intent: "reject";
  jobId: string;
  error?: string;
}

export type TodayActionResult =
  | CompleteActionResult
  | AddJobActionResult
  | AddRecurringJobActionResult
  | ApproveActionResult
  | RejectActionResult;

const selectedMemberKey = "family-jobs-board-member";

async function todayLoader({ request }: LoaderFunctionArgs) {
  const memberFromUrl = new URL(request.url).searchParams.get("member");
  const memberId =
    memberFromUrl ?? window.localStorage.getItem(selectedMemberKey);
  return getToday(memberId);
}

async function todayAction({
  request,
}: ActionFunctionArgs): Promise<TodayActionResult> {
  const form = await request.formData();
  if (form.get("intent") === "add") {
    return addJobAction(form);
  }
  if (form.get("intent") === "addRecurring") {
    return addRecurringJobAction(form);
  }
  if (form.get("intent") === "approve") {
    return approveAction(form);
  }
  if (form.get("intent") === "reject") {
    return rejectAction(form);
  }

  return completeAction(form);
}

async function addRecurringJobAction(
  form: FormData,
): Promise<AddRecurringJobActionResult> {
  const submittedRequestId = form.get("requestId");
  const requestId =
    typeof submittedRequestId === "string" && submittedRequestId.length > 0
      ? submittedRequestId
      : crypto.randomUUID();
  const viewerId = form.get("viewerId");
  const recurrenceFrequency = form.get("recurrenceFrequency");
  const childId = form.get("childId");
  const name = form.get("name");
  const description = form.get("description");
  const pointsValue = form.get("points");
  const agendaPeriod = form.get("agendaPeriod");
  const scheduledTime = form.get("scheduledTime");
  const startDate = form.get("startDate");
  const endDate = form.get("endDate");
  const weekdays = form
    .getAll("weekdays")
    .filter((value): value is string => typeof value === "string");
  const dayOfMonthValue = form.get("dayOfMonth");
  const dayOfMonth = Number(dayOfMonthValue);
  const points = Number(pointsValue);
  const validAgendaPeriods = [
    "morning",
    "arrivingHome",
    "evening",
    "unscheduled",
  ] as const;

  if (
    typeof viewerId !== "string" ||
    (recurrenceFrequency !== "daily" &&
      recurrenceFrequency !== "weekly" &&
      recurrenceFrequency !== "monthly") ||
    typeof childId !== "string" ||
    typeof name !== "string" ||
    name.trim().length === 0 ||
    name.trim().length > 160 ||
    typeof description !== "string" ||
    description.trim().length > 1000 ||
    typeof pointsValue !== "string" ||
    !Number.isInteger(points) ||
    points < 0 ||
    typeof agendaPeriod !== "string" ||
    !validAgendaPeriods.some((value) => value === agendaPeriod) ||
    typeof startDate !== "string" ||
    startDate.length === 0 ||
    (typeof endDate === "string" &&
      endDate.length > 0 &&
      endDate < startDate) ||
    (recurrenceFrequency === "weekly" && weekdays.length === 0) ||
    (recurrenceFrequency === "monthly" &&
      (typeof dayOfMonthValue !== "string" ||
        !Number.isInteger(dayOfMonth) ||
        dayOfMonth < 1 ||
        dayOfMonth > 31))
  ) {
    return {
      intent: "addRecurring",
      error: "Check the recurring job details and try again.",
    };
  }

  try {
    const recurringRequest = {
      requestId,
      viewerId,
      childId,
      name,
      description,
      points,
      agendaPeriod: agendaPeriod as (typeof validAgendaPeriods)[number],
      scheduledTime:
        typeof scheduledTime === "string" && scheduledTime.length > 0
          ? scheduledTime
          : null,
      startDate,
      endDate:
        typeof endDate === "string" && endDate.length > 0 ? endDate : null,
    };
    let result;
    if (recurrenceFrequency === "monthly") {
      result = await createMonthlyRecurringJob({
        ...recurringRequest,
        dayOfMonth,
      });
    } else if (recurrenceFrequency === "weekly") {
      result = await createWeeklyRecurringJob({
        ...recurringRequest,
        weekdays,
      });
    } else {
      result = await createDailyRecurringJob(recurringRequest);
    }
    return {
      intent: "addRecurring",
      frequency: recurrenceFrequency,
      success: true,
      generatedThrough: result.generatedThrough,
    };
  } catch (error) {
    return {
      intent: "addRecurring",
      error:
        error instanceof ApiError
          ? error.message
          : "That recurring job couldn't be created.",
    };
  }
}

async function rejectAction(form: FormData): Promise<RejectActionResult> {
  const jobId = form.get("jobId");
  const reason = form.get("reason");
  if (typeof jobId !== "string") {
    return {
      intent: "reject",
      jobId: "",
      error: "The selected job was missing.",
    };
  }

  try {
    await rejectJob(jobId, typeof reason === "string" ? reason : null);
    return { intent: "reject", jobId };
  } catch (error) {
    return {
      intent: "reject",
      jobId,
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be rejected.",
    };
  }
}

async function approveAction(form: FormData): Promise<ApproveActionResult> {
  const jobId = form.get("jobId");
  if (typeof jobId !== "string") {
    return {
      intent: "approve",
      jobId: "",
      error: "The selected job was missing.",
    };
  }

  try {
    await approveJob(jobId);
    return { intent: "approve", jobId };
  } catch (error) {
    return {
      intent: "approve",
      jobId,
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be approved.",
    };
  }
}

async function completeAction(form: FormData): Promise<CompleteActionResult> {
  const jobId = form.get("jobId");
  if (typeof jobId !== "string") {
    return {
      intent: "complete",
      jobId: "",
      error: "The selected job was missing.",
    };
  }

  try {
    await completeJob(jobId);
    return { intent: "complete", jobId };
  } catch (error) {
    return {
      intent: "complete",
      jobId,
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be completed.",
    };
  }
}

async function addJobAction(form: FormData): Promise<AddJobActionResult> {
  const name = form.get("name");
  const description = form.get("description");
  const pointsValue = form.get("points");
  const childId = form.get("childId");
  const points = Number(pointsValue);

  if (typeof childId !== "string" || childId.length === 0) {
    return { intent: "add", error: "Choose a child." };
  }

  if (typeof name !== "string" || name.trim().length === 0) {
    return { intent: "add", error: "Enter a job name." };
  }
  if (name.trim().length > 160) {
    return {
      intent: "add",
      error: "The job name must be 160 characters or fewer.",
    };
  }
  if (typeof description !== "string" || description.trim().length > 1000) {
    return {
      intent: "add",
      error: "The description must be 1000 characters or fewer.",
    };
  }
  if (
    typeof pointsValue !== "string" ||
    pointsValue.trim().length === 0 ||
    !Number.isInteger(points) ||
    points < 0
  ) {
    return { intent: "add", error: "Enter zero or more whole points." };
  }

  try {
    await addJob({ childId, name, description, points });
    return { intent: "add", success: true };
  } catch (error) {
    return {
      intent: "add",
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be added.",
    };
  }
}

export const routes: RouteObject[] = [
  {
    path: "/",
    loader: todayLoader,
    action: todayAction,
    Component: TodayPage,
    HydrateFallback: LoadingPage,
    shouldRevalidate: ({ actionResult, defaultShouldRevalidate }) => {
      if (
        actionResult &&
        typeof actionResult === "object" &&
        "error" in actionResult &&
        actionResult.error
      ) {
        return false;
      }

      return defaultShouldRevalidate;
    },
  },
];
