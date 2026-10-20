import type { ActionFunctionArgs } from "react-router";

import {
  TurnRotationApiError,
  endTurnRotation,
  getTurnRotations,
  saveTurnRotation,
  takeTurn,
  undoTurn,
} from "../../api/turnRotation";
import type { TurnRotationOverview } from "../../api/turnRotation";
import { quoted } from "../../app/actionDescriptions";
import { createRequestId } from "../../app/requestId";
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

/**
 * The outcome of ticking a turn off or unticking one from the board. A tick
 * waits behind the undo toast like other actions; a failure found before
 * queueing is reported as `failure` rather than `error` so the board still
 * reloads and shows where the turn is now.
 */
export interface WhoseTurnTickResult {
  intent: "take" | "undo";
  rotationId: string;
  queued?: true;
  failure?: string | undefined;
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
  const advanceMode =
    text(form, "advanceMode") === "whenTicked" ? "whenTicked" : "daily";
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
          advanceMode,
        }),
      failure,
    },
  );
}

export async function whoseTurnTickAction({
  request,
}: ActionFunctionArgs): Promise<WhoseTurnTickResult> {
  const form = await request.formData();
  const intent = text(form, "intent") === "undo" ? "undo" : "take";
  const rotationId = text(form, "rotationId");
  const childName = text(form, "childName") || "the child";
  const question = quoted(text(form, "question") || "Whose turn");

  if (intent === "undo") {
    const turnId = text(form, "turnId");
    if (!rotationId || !turnId) {
      return { intent, rotationId, failure: "That turn couldn't be unticked." };
    }

    return queueUndoableAction(
      { intent, rotationId },
      {
        form,
        description: `Unticked ${childName}’s turn: ${question}`,
        commit: () => undoTurn(rotationId, turnId),
        failure: (error) =>
          errorMessage(error, "That turn couldn't be unticked."),
      },
    );
  }

  // Chosen now so Try again resends the same tick rather than a second one.
  let requestId: string;
  try {
    requestId = createRequestId();
  } catch {
    return {
      intent,
      rotationId,
      failure:
        "Your browser couldn't prepare this request. Try again or use another browser.",
    };
  }
  const tick = {
    requestId,
    revisionId: text(form, "revisionId"),
    turnNumber: Number(text(form, "turnNumber")),
    childId: text(form, "childId"),
  };
  return queueUndoableAction(
    { intent, rotationId },
    {
      form,
      description: `Ticked off ${childName}’s turn: ${question}`,
      commit: () => takeTurn(rotationId, tick),
      failure: (error) =>
        errorMessage(error, "That turn couldn't be ticked off."),
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
