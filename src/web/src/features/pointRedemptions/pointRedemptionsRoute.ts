import type { ActionFunctionArgs } from "react-router";

import { PointsLedgerApiError, getPointsLedger } from "../../api/pointsLedger";
import {
  PointRedemptionApiError,
  redeemPoints,
} from "../../api/pointRedemptions";
import { childName, pointsText } from "../../app/actionDescriptions";
import { createRequestId } from "../../app/requestId";
import { queueUndoableAction } from "../../app/undoQueue";

export const maximumRewardLength = 200;

export interface PointRedemptionsLoaderData {
  balances?: Record<string, number>;
  error?: string;
}

export interface PointRedemptionActionResult {
  intent: "redeem";
  /** Set when the redemption passed validation and waits behind an undo toast. */
  queued?: true;
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
  const pointsValue = text(form, "points").trim();
  const points = Number(pointsValue);
  const requestId = text(form, "requestId") || safeRequestId();

  if (childId.length === 0) {
    return fail("Choose a child.");
  }
  if (pointsValue === "" || !Number.isInteger(points) || points < 1) {
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

  return queueUndoableAction(
    { intent: "redeem" },
    {
      form,
      description: `${childName(form, childId)} spent ${pointsText(points)} on ${reward}`,
      commit: () => redeemPoints({ requestId, childId, points, reward }),
      failure: (error) =>
        error instanceof PointRedemptionApiError
          ? error.message
          : "That redemption couldn't be saved.",
    },
  );
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
