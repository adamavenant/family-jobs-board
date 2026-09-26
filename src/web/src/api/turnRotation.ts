import createClient from "openapi-fetch";

import type { paths } from "./schema";
import { authenticatedFetch } from "./auth";

export interface TurnRotationConfiguration {
  id: string;
  effectiveFrom: string;
  question: string;
  participantChildIds: string[];
  firstChildId: string;
  createdByMemberId: string;
  createdAtUtc: string;
}

export interface TurnRotationTurn {
  date: string;
  question: string;
  childId: string;
  childDisplayName: string;
}

export interface TurnRotationOverview {
  current: TurnRotationConfiguration | null;
  upcomingTurns: TurnRotationTurn[];
  hasRevisions: boolean;
}

export interface SaveTurnRotationInput {
  participantChildIds: string[];
  firstChildId: string;
  effectiveFrom: string;
  question: string | null;
}

export class TurnRotationApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "TurnRotationApiError";
  }
}

export async function getTurnRotation(): Promise<TurnRotationOverview> {
  const { data, error } = await apiClient().GET("/api/turn-rotation");
  if (!data) {
    throw new TurnRotationApiError(
      problemMessage(error, "We couldn't load the whose-turn rotation."),
    );
  }

  return data;
}

export async function saveTurnRotation(
  input: SaveTurnRotationInput,
): Promise<TurnRotationOverview> {
  const { data, error } = await apiClient().PUT("/api/turn-rotation", {
    body: input,
  });
  if (!data) {
    throw new TurnRotationApiError(
      problemMessage(error, "That rotation couldn't be saved."),
    );
  }

  return data;
}

function apiClient() {
  return createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object") {
    const detail = "detail" in error ? error.detail : undefined;
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }

    const errors = "errors" in error ? error.errors : undefined;
    if (errors && typeof errors === "object") {
      for (const messages of Object.values(errors)) {
        if (Array.isArray(messages) && typeof messages[0] === "string") {
          return messages[0];
        }
      }
    }
  }

  return fallback;
}
