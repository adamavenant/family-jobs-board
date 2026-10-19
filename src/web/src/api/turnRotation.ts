import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

export type TurnAdvanceMode = "daily" | "whenTicked";

export type TurnRotationConfiguration =
  components["schemas"]["TurnRotationConfigurationResponse"];

export type TurnRotationTurn =
  components["schemas"]["TurnRotationTurnResponse"];

export type TurnRotationSummary =
  components["schemas"]["TurnRotationSummaryResponse"];

export type TurnRotationOverview =
  components["schemas"]["TurnRotationOverviewResponse"];

export type TakenTurn = components["schemas"]["TakenTurnResponse"];

/** Names the turn the board showed, so a stale or replayed tick never ticks again. */
export interface TakeTurnInput {
  requestId: string;
  revisionId: string;
  turnNumber: number;
  childId: string;
}

export interface SaveTurnRotationInput {
  participantChildIds: string[];
  firstChildId: string;
  effectiveFrom: string;
  question: string | null;
  advanceMode: TurnAdvanceMode;
}

export class TurnRotationApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "TurnRotationApiError";
  }
}

export async function getTurnRotations(): Promise<TurnRotationOverview> {
  const { data, error } = await apiClient().GET("/api/turn-rotations");
  if (!data) {
    throw new TurnRotationApiError(
      problemMessage(error, "We couldn't load the whose-turn rotations."),
    );
  }

  return data;
}

export async function saveTurnRotation(
  rotationId: string | null,
  input: SaveTurnRotationInput,
): Promise<TurnRotationOverview> {
  const { data, error } = rotationId
    ? await apiClient().PUT("/api/turn-rotations/{rotationId}", {
        params: { path: { rotationId } },
        body: input,
      })
    : await apiClient().POST("/api/turn-rotations", { body: input });
  if (!data) {
    throw new TurnRotationApiError(
      problemMessage(error, "That rotation couldn't be saved."),
    );
  }

  return data;
}

export async function endTurnRotation(
  rotationId: string,
): Promise<TurnRotationOverview> {
  const { data, error } = await apiClient().DELETE(
    "/api/turn-rotations/{rotationId}",
    { params: { path: { rotationId } } },
  );
  if (!data) {
    throw new TurnRotationApiError(
      problemMessage(error, "That rotation couldn't be ended."),
    );
  }

  return data;
}

export async function takeTurn(
  rotationId: string,
  input: TakeTurnInput,
): Promise<TakenTurn> {
  const { data, error } = await apiClient().POST(
    "/api/turn-rotations/{rotationId}/turns",
    { params: { path: { rotationId } }, body: input },
  );
  if (!data) {
    throw new TurnRotationApiError(
      problemMessage(error, "That turn couldn't be ticked off."),
    );
  }

  return data;
}

export async function undoTurn(
  rotationId: string,
  turnId: string,
): Promise<TakenTurn> {
  const { data, error } = await apiClient().DELETE(
    "/api/turn-rotations/{rotationId}/turns/{turnId}",
    { params: { path: { rotationId, turnId } } },
  );
  if (!data) {
    throw new TurnRotationApiError(
      problemMessage(error, "That turn couldn't be unticked."),
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
