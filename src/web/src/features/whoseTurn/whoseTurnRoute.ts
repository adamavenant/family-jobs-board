import type { ActionFunctionArgs } from "react-router";

import {
  TurnRotationApiError,
  getTurnRotation,
  saveTurnRotation,
} from "../../api/turnRotation";
import type { TurnRotationOverview } from "../../api/turnRotation";

export interface WhoseTurnActionResult {
  overview?: TurnRotationOverview | undefined;
  saved?: boolean | undefined;
  error?: string | undefined;
}

export async function whoseTurnLoader(): Promise<WhoseTurnActionResult> {
  try {
    return { overview: await getTurnRotation() };
  } catch (error) {
    return {
      error: errorMessage(error, "We couldn't load the whose-turn rotation."),
    };
  }
}

export async function whoseTurnAction({
  request,
}: ActionFunctionArgs): Promise<WhoseTurnActionResult> {
  const form = await request.formData();
  const participantChildIds = form
    .getAll("participantChildIds")
    .filter((value): value is string => typeof value === "string");
  const firstChildId = text(form, "firstChildId");
  const effectiveFrom = text(form, "effectiveFrom");
  const questionRaw = text(form, "question").trim();
  const question = questionRaw === "" ? null : questionRaw;

  if (participantChildIds.length === 0 || !firstChildId || !effectiveFrom) {
    return {
      overview: await reloadOverview(),
      error:
        "Choose one or more children, a first turn, and an effective date.",
    };
  }

  try {
    const overview = await saveTurnRotation({
      participantChildIds,
      firstChildId,
      effectiveFrom,
      question,
    });
    return { overview, saved: true };
  } catch (error) {
    return {
      overview: await reloadOverview(),
      error: errorMessage(error, "That rotation couldn't be saved."),
    };
  }
}

async function reloadOverview(): Promise<TurnRotationOverview | undefined> {
  try {
    return await getTurnRotation();
  } catch {
    return undefined;
  }
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === "string" ? value : "";
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof TurnRotationApiError ? error.message : fallback;
}
