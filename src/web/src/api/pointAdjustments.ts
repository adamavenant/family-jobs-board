import createClient from "openapi-fetch";

import type { paths } from "./schema";
import { authenticatedFetch } from "./auth";

export interface PointAdjustmentInput {
  requestId: string;
  childId: string;
  amount: number;
  reason: string;
  confirmNegativeBalance: boolean;
}

export interface NegativeBalanceWarning {
  message: string;
  currentBalance: number;
  resultingBalance: number;
}

export type RecordPointAdjustmentResult =
  | { status: "recorded"; amount: number; pointsBalance: number }
  | ({ status: "needsConfirmation" } & NegativeBalanceWarning);

export class PointAdjustmentApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "PointAdjustmentApiError";
  }
}

export async function recordPointAdjustment(
  input: PointAdjustmentInput,
): Promise<RecordPointAdjustmentResult> {
  const client = createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
  const { data, error } = await client.POST("/api/point-adjustments", {
    body: input,
  });
  if (data) {
    return {
      status: "recorded",
      amount: Number(data.adjustment.amount),
      pointsBalance: Number(data.pointsBalance),
    };
  }

  const warning = negativeBalanceWarning(error);
  if (warning) {
    return { status: "needsConfirmation", ...warning };
  }

  throw new PointAdjustmentApiError(
    problemMessage(error, "That points adjustment couldn't be saved."),
  );
}

function negativeBalanceWarning(error: unknown): NegativeBalanceWarning | null {
  if (
    error &&
    typeof error === "object" &&
    Reflect.get(error, "code") === "negativeBalanceConfirmationRequired"
  ) {
    const currentBalance = Number(Reflect.get(error, "currentBalance"));
    const resultingBalance = Number(Reflect.get(error, "resultingBalance"));
    const detail = Reflect.get(error, "detail");
    if (
      Number.isInteger(currentBalance) &&
      Number.isInteger(resultingBalance)
    ) {
      return {
        message:
          typeof detail === "string" && detail.length > 0
            ? detail
            : `This would take the balance from ${currentBalance} to ${resultingBalance}.`,
        currentBalance,
        resultingBalance,
      };
    }
  }

  return null;
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object") {
    const errors = Reflect.get(error, "errors");
    if (errors && typeof errors === "object") {
      for (const messages of Object.values(errors)) {
        if (Array.isArray(messages) && typeof messages[0] === "string") {
          return messages[0];
        }
      }
    }

    const detail = Reflect.get(error, "detail");
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }
  }

  return fallback;
}
