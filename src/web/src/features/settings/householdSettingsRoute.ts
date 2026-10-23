import type { ActionFunctionArgs } from "react-router";

import {
  HouseholdSettingsApiError,
  getHouseholdSettings,
  updateHouseholdSettings,
} from "../../api/householdSettings";
import type { HouseholdSettings } from "../../api/householdSettings";
import {
  maximumUndoGracePeriodSeconds,
  minimumUndoGracePeriodSeconds,
  undoQueue,
} from "../../app/undoQueue";

export interface HouseholdSettingsLoaderData {
  settings?: HouseholdSettings;
  error?: string;
}

export interface HouseholdSettingsActionResult {
  intent: "saveUndoTime";
  settings?: HouseholdSettings;
  error?: string;
}

export async function householdSettingsLoader(): Promise<HouseholdSettingsLoaderData> {
  try {
    return { settings: await getHouseholdSettings() };
  } catch (error) {
    return {
      error:
        error instanceof HouseholdSettingsApiError
          ? error.message
          : "We couldn't load the household settings.",
    };
  }
}

// Changing the undo time itself saves straight away: it is a setting, not one
// of the household actions the undo toast covers.
export async function householdSettingsAction({
  request,
}: ActionFunctionArgs): Promise<HouseholdSettingsActionResult> {
  const form = await request.formData();
  const value = form.get("undoGracePeriodSeconds");
  const seconds = Number(value);
  if (
    typeof value !== "string" ||
    value.trim() === "" ||
    !Number.isInteger(seconds) ||
    seconds < minimumUndoGracePeriodSeconds ||
    seconds > maximumUndoGracePeriodSeconds
  ) {
    return {
      intent: "saveUndoTime",
      error: `Choose whole seconds from ${minimumUndoGracePeriodSeconds} to ${maximumUndoGracePeriodSeconds}.`,
    };
  }

  try {
    const settings = await updateHouseholdSettings({
      undoGracePeriodSeconds: seconds,
    });
    undoQueue.setGracePeriodSeconds(settings.undoGracePeriodSeconds);
    return { intent: "saveUndoTime", settings };
  } catch (error) {
    return {
      intent: "saveUndoTime",
      error:
        error instanceof HouseholdSettingsApiError
          ? error.message
          : "The undo time couldn't be saved.",
    };
  }
}
