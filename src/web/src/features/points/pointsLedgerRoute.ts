import type { LoaderFunctionArgs } from "react-router";
import { redirect } from "react-router";

import { currentSession, refreshSession } from "../../api/auth";
import type { AuthMember } from "../../api/auth";
import { getPointsLedger, PointsLedgerApiError } from "../../api/pointsLedger";
import type { PointsLedger } from "../../api/pointsLedger";

export type PointsLedgerLoaderData =
  | { state: "ready"; viewer: AuthMember; ledger: PointsLedger }
  | { state: "error"; viewer: AuthMember; error: string };

export async function pointsLedgerLoader({
  request,
}: LoaderFunctionArgs): Promise<PointsLedgerLoaderData> {
  // A reload loses the in-memory session, so restore it before deciding to leave.
  const session = currentSession() ?? (await refreshSession());
  if (!session) {
    throw redirect("/");
  }

  const viewer = session.member;
  const searchParams = new URL(request.url).searchParams;
  // Children always see their own ledger, so a child filter in the URL is ignored.
  const childId =
    viewer.role === "adult"
      ? (searchParams.get("childId") ?? undefined)
      : undefined;
  const before = searchParams.get("before") ?? undefined;

  try {
    return {
      state: "ready",
      viewer,
      ledger: await getPointsLedger({ childId, before }),
    };
  } catch (error) {
    return {
      state: "error",
      viewer,
      error:
        error instanceof PointsLedgerApiError
          ? error.message
          : "We couldn't load the points.",
    };
  }
}

export function pointsHref(childId: string | null, before?: string): string {
  const params = new URLSearchParams();
  if (childId) {
    params.set("childId", childId);
  }
  if (before) {
    params.set("before", before);
  }
  const query = params.toString();
  return query ? `/points?${query}` : "/points";
}
