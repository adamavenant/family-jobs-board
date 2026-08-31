import type { ActionFunctionArgs } from "react-router";
import type { RouteObject } from "react-router";

import { ApiError, addJob, completeJob, getToday } from "../api/today";
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

export type TodayActionResult = CompleteActionResult | AddJobActionResult;

async function todayLoader() {
  return getToday();
}

async function todayAction({
  request,
}: ActionFunctionArgs): Promise<TodayActionResult> {
  const form = await request.formData();
  if (form.get("intent") === "add") {
    return addJobAction(form);
  }

  return completeAction(form);
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
  const points = Number(pointsValue);

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
    await addJob({ name, description, points });
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
