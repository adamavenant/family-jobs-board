import type { ActionFunctionArgs } from "react-router";

import {
  PointAdjustmentApiError,
  recordPointAdjustment,
} from "../../api/pointAdjustments";
import { createRequestId } from "../../app/requestId";

export interface PointAdjustmentActionResult {
  intent: "recordAdjustment";
  recorded?: { childId: string; amount: number; pointsBalance: number };
  error?: string;
}

export async function pointAdjustmentsAction({
  request,
}: ActionFunctionArgs): Promise<PointAdjustmentActionResult> {
  const form = await request.formData();
  const childId = text(form, "childId");
  const reason = text(form, "reason").trim();
  const magnitude = Number(text(form, "amount"));
  const direction = text(form, "direction");
  const requestId = text(form, "requestId") || safeRequestId();

  if (childId.length === 0) {
    return fail("Choose a child.");
  }
  if (direction !== "add" && direction !== "remove") {
    return fail("Choose whether to add or remove points.");
  }
  if (
    text(form, "amount").trim() === "" ||
    !Number.isInteger(magnitude) ||
    magnitude < 1
  ) {
    return fail("Enter a whole number of points, at least 1.");
  }
  if (reason.length === 0) {
    return fail("Enter a reason.");
  }
  if (reason.length > 500) {
    return fail("The reason must be 500 characters or fewer.");
  }
  if (!requestId) {
    return fail(
      "Your browser couldn't prepare this request. Try again or use another browser.",
    );
  }

  try {
    const result = await recordPointAdjustment({
      requestId,
      childId,
      amount: direction === "remove" ? -magnitude : magnitude,
      reason,
    });
    return {
      intent: "recordAdjustment",
      recorded: {
        childId,
        amount: result.amount,
        pointsBalance: result.pointsBalance,
      },
    };
  } catch (error) {
    return fail(
      error instanceof PointAdjustmentApiError
        ? error.message
        : "That points adjustment couldn't be saved.",
    );
  }
}

function fail(error: string): PointAdjustmentActionResult {
  return { intent: "recordAdjustment", error };
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
