import type { ActionFunctionArgs } from "react-router";

import { PointsLedgerApiError, getPointsLedger } from "../../api/pointsLedger";
import {
  PointRedemptionApiError,
  redeemPoints,
} from "../../api/pointRedemptions";
import { createRequestId } from "../../app/requestId";

export const maximumRewardLength = 200;

export interface PointRedemptionsLoaderData {
  balances?: Record<string, number>;
  error?: string;
}

export interface PointRedemptionActionResult {
  intent: "redeem";
  redeemed?: {
    childId: string;
    points: number;
    reward: string;
    pointsBalance: number;
  };
  error?: string;
}

/** Each child's current balance, read from the points ledger. */
export async function pointRedemptionsLoader(): Promise<PointRedemptionsLoaderData> {
  try {
    const ledger = await getPointsLedger({});
    return {
      balances: Object.fromEntries(
        ledger.children.map((child) => [child.id, child.balance]),
      ),
    };
  } catch (error) {
    return {
      error:
        error instanceof PointsLedgerApiError
          ? error.message
          : "We couldn't load the points balances.",
    };
  }
}

export async function pointRedemptionsAction({
  request,
}: ActionFunctionArgs): Promise<PointRedemptionActionResult> {
  const form = await request.formData();
  const childId = text(form, "childId");
  const reward = text(form, "reward").trim();
  const pointsText = text(form, "points").trim();
  const points = Number(pointsText);
  const requestId = text(form, "requestId") || safeRequestId();

  if (childId.length === 0) {
    return fail("Choose a child.");
  }
  if (pointsText === "" || !Number.isInteger(points) || points < 1) {
    return fail("Enter a whole number of points, at least 1.");
  }
  if (reward.length === 0) {
    return fail("Enter the reward.");
  }
  if (reward.length > maximumRewardLength) {
    return fail(
      `The reward must be ${maximumRewardLength} characters or fewer.`,
    );
  }
  if (!requestId) {
    return fail(
      "Your browser couldn't prepare this request. Try again or use another browser.",
    );
  }

  try {
    const result = await redeemPoints({ requestId, childId, points, reward });
    return {
      intent: "redeem",
      redeemed: {
        childId,
        points: result.points,
        reward: result.reward,
        pointsBalance: result.pointsBalance,
      },
    };
  } catch (error) {
    return fail(
      error instanceof PointRedemptionApiError
        ? error.message
        : "That redemption couldn't be saved.",
    );
  }
}

function fail(error: string): PointRedemptionActionResult {
  return { intent: "redeem", error };
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === "string" ? value : "";
}

function safeRequestId(): string {
  try {
    return createRequestId();
  } catch {
    return "";
  }
}
