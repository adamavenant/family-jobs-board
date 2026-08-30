import type { ActionFunctionArgs } from "react-router";
import type { RouteObject } from "react-router";

import { ApiError, completeJob, getToday, addJob } from "../api/today";
import { LoadingPage } from "./LoadingPage";
import { TodayPage } from "../features/today/TodayPage";

export interface CompleteActionResult {
  jobId: string;
  error?: string;
}

async function todayLoader() {
  return getToday();
}

async function completeAction({
  request,
}: ActionFunctionArgs): Promise<CompleteActionResult> {
  const form = await request.formData();
  const jobId = form.get("jobId");
  if (typeof jobId !== "string") {
    return { jobId: "", error: "The selected job was missing." };
  }

  try {
    await completeJob(jobId);
    return { jobId };
  } catch (error) {
    return {
      jobId,
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be completed.",
    };
  }
}

async function addJobAction({
  request,
}: ActionFunctionArgs): Promise<{ success?: boolean; error?: string }> {
  const form = await request.formData();
  const name = form.get("name") as string;
  const description = form.get("description") as string;
  const points = Number(form.get("points"));
  
  if (!name || !description || isNaN(points)) {
    return { 
      error: "Please fill in all fields with valid values." 
    };
  }

  try {
    await addJob({ name, description, points });
    return { success: true };
  } catch (error) {
    return { 
      error: "Failed to add job. Please check the details and try again." 
    };
  }
}

export const routes: RouteObject[] = [
  {
    path: "/",
    loader: todayLoader,
    action: completeAction,
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
