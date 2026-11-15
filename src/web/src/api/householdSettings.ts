import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

type HouseholdSettingsResponse =
  components["schemas"]["HouseholdSettingsResponse"];

export interface HouseholdSettings {
  undoGracePeriodSeconds: number;
  minimumUndoGracePeriodSeconds: number;
  maximumUndoGracePeriodSeconds: number;
}

export class HouseholdSettingsApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "HouseholdSettingsApiError";
  }
}

export async function getHouseholdSettings(): Promise<HouseholdSettings> {
  const { data, error } = await apiClient().GET("/api/household-settings");
  if (!data) {
    throw new HouseholdSettingsApiError(
      problemMessage(error, "We couldn't load the household settings."),
    );
  }

  return mapSettings(data);
}

export async function updateHouseholdSettings(input: {
  undoGracePeriodSeconds: number;
}): Promise<HouseholdSettings> {
  const { data, error } = await apiClient().PUT("/api/household-settings", {
    body: input,
  });
  if (!data) {
    throw new HouseholdSettingsApiError(
      problemMessage(error, "The undo time couldn't be saved."),
    );
  }

  return mapSettings(data);
}

function mapSettings(data: HouseholdSettingsResponse): HouseholdSettings {
  return {
    undoGracePeriodSeconds: Number(data.undoGracePeriodSeconds),
    minimumUndoGracePeriodSeconds: Number(data.minimumUndoGracePeriodSeconds),
    maximumUndoGracePeriodSeconds: Number(data.maximumUndoGracePeriodSeconds),
  };
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
