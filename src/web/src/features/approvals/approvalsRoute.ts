import { ApprovalsApiError, getPendingApprovals } from "../../api/approvals";
import type { TodayJob } from "../../api/today";

export type ApprovalsLoaderData = { jobs: TodayJob[] } | { error: string };

export async function approvalsLoader(): Promise<ApprovalsLoaderData> {
  try {
    return { jobs: await getPendingApprovals() };
  } catch (error) {
    return {
      error:
        error instanceof ApprovalsApiError
          ? error.message
          : "We couldn't load the jobs awaiting review.",
    };
  }
}
