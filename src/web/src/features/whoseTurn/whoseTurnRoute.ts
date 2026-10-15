import type { ActionFunctionArgs } from "react-router";

import {
  TurnRotationApiError,
  endTurnRotation,
  getTurnRotations,
  saveTurnRotation,
} from "../../api/turnRotation";
import type { TurnRotationOverview } from "../../api/turnRotation";
import { quoted } from "../../app/actionDescriptions";
import { queueUndoableAction } from "../../app/undoQueue";

export interface WhoseTurnLoaderData {
  overview?: TurnRotationOverview | undefined;
  error?: string | undefined;
}

export interface WhoseTurnActionResult {
  intent?: "save" | "end" | undefined;
  rotationId?: string | undefined;
  /** Set when the change passed validation and waits behind an undo toast. */
  queued?: true;
  error?: string | undefined;
}

export async function whoseTurnLoader(): Promise<WhoseTurnLoaderData> {
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
  const title = text(form, "rotationTitle") || "the rotation";
  const failure = (error: unknown) =>
    errorMessage(error, "That change couldn't be saved.");

  if (intent === "end") {
    if (!rotationId) {
      return { intent, error: "Choose a rotation to end." };
    }

    return queueUndoableAction(
      { intent, rotationId },
      {
        form,
        description: `Ended ${quoted(title)}. It stops appearing from tomorrow`,
        commit: () => endTurnRotation(rotationId),
        failure,
      },
    );
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
      error:
        "Choose one or more children, a first turn, and an effective date.",
    };
  }

  const question = questionRaw === "" ? null : questionRaw;
  return queueUndoableAction(
    { intent, rotationId },
    {
      form,
      description: rotationId
        ? `Saved changes to ${quoted(question ?? title)} from ${effectiveFrom}`
        : `Started ${quoted(question ?? "Who is Pink today?")} from ${effectiveFrom}`,
      commit: () =>
        saveTurnRotation(rotationId || null, {
          participantChildIds,
          firstChildId,
          effectiveFrom,
          question,
        }),
      failure,
    },
  );
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === "string" ? value : "";
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof TurnRotationApiError ? error.message : fallback;
}
