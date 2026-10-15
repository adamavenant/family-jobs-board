import type { ActionFunctionArgs } from "react-router";

import {
  GoodBehaviourApiError,
  createGoodBehaviourType,
  deleteGoodBehaviourType,
  getGoodBehaviourTypes,
  logGoodBehaviour,
  updateGoodBehaviourType,
} from "../../api/goodBehaviours";
import type { GoodBehaviourType } from "../../api/goodBehaviours";
import {
  pointsText,
  quoted,
  selectedChildNames,
} from "../../app/actionDescriptions";
import { createRequestId } from "../../app/requestId";
import { queueUndoableAction } from "../../app/undoQueue";

export interface GoodBehavioursLoaderData {
  types?: GoodBehaviourType[] | undefined;
  error?: string | undefined;
}

export interface GoodBehavioursActionResult {
  intent?: "createType" | "updateType" | "deleteType" | "logBehaviour";
  affectedTypeId?: string | undefined;
  /** Set when the change passed validation and waits behind an undo toast. */
  queued?: true;
  error?: string | undefined;
}

export async function goodBehavioursLoader(): Promise<GoodBehavioursLoaderData> {
  try {
    return { types: await getGoodBehaviourTypes() };
  } catch (error) {
    return {
      error: errorMessage(error, "We couldn't load the good behaviours."),
    };
  }
}

export async function goodBehavioursAction({
  request,
}: ActionFunctionArgs): Promise<GoodBehavioursActionResult> {
  const form = await request.formData();
  const intent = form.get("intent");
  if (
    intent !== "createType" &&
    intent !== "updateType" &&
    intent !== "deleteType" &&
    intent !== "logBehaviour"
  ) {
    return { error: "That good behaviour action wasn't recognised." };
  }

  const typeId = text(form, "typeId");
  const failure = (error: unknown) =>
    errorMessage(error, "That good behaviour change couldn't be saved.");
  if (intent === "createType" || intent === "updateType") {
    const input = typeInput(form);
    if (input === null || (intent === "updateType" && !typeId)) {
      return {
        intent,
        affectedTypeId: typeId || undefined,
        error: "Check the good behaviour details.",
      };
    }

    return intent === "createType"
      ? queueUndoableAction(
          { intent },
          {
            form,
            description: `Added good behaviour ${quoted(input.name)}: usually ${pointsText(input.points)}`,
            commit: () => createGoodBehaviourType(input),
            failure,
          },
        )
      : queueUndoableAction(
          { intent, affectedTypeId: typeId },
          {
            form,
            description: `Saved good behaviour ${quoted(input.name)}`,
            commit: () => updateGoodBehaviourType(typeId, input),
            failure,
          },
        );
  }

  if (intent === "deleteType") {
    if (!typeId) {
      return { intent, error: "Choose a good behaviour." };
    }

    return queueUndoableAction(
      { intent, affectedTypeId: typeId },
      {
        form,
        description: `Deleted good behaviour ${quoted(text(form, "typeName"))}. Past points are unchanged`,
        commit: () => deleteGoodBehaviourType(typeId),
        failure,
      },
    );
  }

  return logBehaviour(form, typeId, failure);
}

function logBehaviour(
  form: FormData,
  typeId: string,
  failure: (error: unknown) => string,
): GoodBehavioursActionResult {
  const childIds = form
    .getAll("childIds")
    .filter((value): value is string => typeof value === "string");
  const pointsValue = text(form, "points").trim();
  const points = pointsValue === "" ? null : Number(pointsValue);
  const requestId = text(form, "requestId") || safeRequestId();
  if (childIds.length === 0 || new Set(childIds).size !== childIds.length) {
    return { intent: "logBehaviour", error: "Choose one or more children." };
  }
  if (!typeId || (points !== null && !isWholeNumber(points))) {
    return {
      intent: "logBehaviour",
      error: "Choose a good behaviour with zero or more points.",
    };
  }
  if (!requestId) {
    return {
      intent: "logBehaviour",
      error:
        "Your browser couldn't prepare this request. Try again or use another browser.",
    };
  }

  const awarded = points ?? Number(text(form, "typePoints"));
  const each = childIds.length > 1 ? " each" : "";
  return queueUndoableAction(
    { intent: "logBehaviour", affectedTypeId: typeId },
    {
      form,
      description: `Logged ${quoted(text(form, "typeName"))} for ${selectedChildNames(form)}: +${pointsText(awarded)}${each}`,
      commit: () =>
        logGoodBehaviour({
          requestId,
          typeId,
          childIds,
          points,
        }),
      failure,
    },
  );
}

function typeInput(form: FormData) {
  const name = text(form, "name").trim();
  const description = text(form, "description").trim();
  const points = Number(text(form, "points"));
  if (
    name.length === 0 ||
    name.length > 100 ||
    description.length > 500 ||
    text(form, "points").trim() === "" ||
    !isWholeNumber(points)
  ) {
    return null;
  }

  return { name, description, points };
}

function isWholeNumber(value: number): boolean {
  return Number.isInteger(value) && value >= 0;
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

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof GoodBehaviourApiError ? error.message : fallback;
}
