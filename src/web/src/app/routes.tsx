import type { ActionFunctionArgs } from "react-router";
import type { RouteObject } from "react-router";

import { ApiError, completeJob, getToday } from "../api/today";
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
