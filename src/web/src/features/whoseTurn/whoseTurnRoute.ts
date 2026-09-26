import type { ActionFunctionArgs } from "react-router";

import {
  TurnRotationApiError,
  endTurnRotation,
  getTurnRotations,
  saveTurnRotation,
} from "../../api/turnRotation";
import type { TurnRotationOverview } from "../../api/turnRotation";

export interface WhoseTurnActionResult {
  overview?: TurnRotationOverview | undefined;
  intent?: "save" | "end" | undefined;
  rotationId?: string | undefined;
  message?: string | undefined;
  error?: string | undefined;
}

export async function whoseTurnLoader(): Promise<WhoseTurnActionResult> {
  try {
    return { overview: await getTurnRotations() };
  } catch (error) {
    return {
      error: errorMessage(error, "We couldn't load the whose-turn rotations."),
    };
  }
}

export async function whoseTurnAction({
  request,
}: ActionFunctionArgs): Promise<WhoseTurnActionResult> {
  const form = await request.formData();
  const intent = text(form, "intent") === "end" ? "end" : "save";
  const rotationId = text(form, "rotationId");

  try {
    if (intent === "end") {
      if (!rotationId) {
        return { intent, error: "Choose a rotation to end." };
      }

      const overview = await endTurnRotation(rotationId);
      return {
        intent,
        rotationId,
        overview,
        message: "Rotation ended. It stops appearing from tomorrow.",
      };
    }

    const participantChildIds = form
      .getAll("participantChildIds")
      .filter((value): value is string => typeof value === "string");
    const firstChildId = text(form, "firstChildId");
    const effectiveFrom = text(form, "effectiveFrom");
    const questionRaw = text(form, "question").trim();
    if (participantChildIds.length === 0 || !firstChildId || !effectiveFrom) {
      return {
        intent,
        rotationId,
        overview: await reloadOverview(),
        error:
          "Choose one or more children, a first turn, and an effective date.",
      };
    }

    const overview = await saveTurnRotation(rotationId || null, {
      participantChildIds,
      firstChildId,
      effectiveFrom,
      question: questionRaw === "" ? null : questionRaw,
    });
    return {
      intent,
      rotationId,
      overview,
      message: "Whose Turn Is It? rotation saved.",
    };
  } catch (error) {
    return {
      intent,
      rotationId,
      overview: await reloadOverview(),
      error: errorMessage(error, "That change couldn't be saved."),
    };
  }
}

async function reloadOverview(): Promise<TurnRotationOverview | undefined> {
  try {
    return await getTurnRotations();
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
