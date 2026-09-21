import type { ActionFunctionArgs } from "react-router";

import {
  GoodBehaviourApiError,
  createGoodBehaviourType,
  deleteGoodBehaviourType,
  getGoodBehaviourTypes,
  logGoodBehaviour,
  updateGoodBehaviourType,
} from "../../api/goodBehaviours";
import type {
  GoodBehaviourType,
  LoggedGoodBehaviour,
} from "../../api/goodBehaviours";
import { createRequestId } from "../../app/requestId";

export interface GoodBehavioursActionResult {
  intent?: "createType" | "updateType" | "deleteType" | "logBehaviour";
  types?: GoodBehaviourType[] | undefined;
  affectedTypeId?: string | undefined;
  message?: string | undefined;
  logged?: LoggedGoodBehaviour | undefined;
  error?: string | undefined;
}

export async function goodBehavioursLoader(): Promise<GoodBehavioursActionResult> {
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
  try {
    if (intent === "createType" || intent === "updateType") {
      const input = typeInput(form);
      if (input === null || (intent === "updateType" && !typeId)) {
        return {
          intent,
          affectedTypeId: typeId || undefined,
          types: await reloadTypes(),
          error: "Check the good behaviour details.",
        };
      }

      const saved =
        intent === "createType"
          ? await createGoodBehaviourType(input)
          : await updateGoodBehaviourType(typeId, input);
      return {
        intent,
        types: await getGoodBehaviourTypes(),
        affectedTypeId: saved.id,
        message:
          intent === "createType"
            ? `Added ${saved.name}.`
            : `Saved ${saved.name}.`,
      };
    }

    if (intent === "deleteType") {
      if (!typeId) {
        return { intent, error: "Choose a good behaviour." };
      }

      await deleteGoodBehaviourType(typeId);
      return {
        intent,
        types: await getGoodBehaviourTypes(),
        affectedTypeId: typeId,
        message: "Good behaviour deleted. Past points are unchanged.",
      };
    }

    return await logBehaviour(form, typeId);
  } catch (error) {
    return {
      intent,
      affectedTypeId: typeId || undefined,
      types: await reloadTypes(),
      error: errorMessage(
        error,
        "That good behaviour change couldn't be saved.",
      ),
    };
  }
}

async function logBehaviour(
  form: FormData,
  typeId: string,
): Promise<GoodBehavioursActionResult> {
  const childIds = form
    .getAll("childIds")
    .filter((value): value is string => typeof value === "string");
  const pointsValue = text(form, "points").trim();
  const points = pointsValue === "" ? null : Number(pointsValue);
  const requestId = text(form, "requestId") || safeRequestId();
  if (childIds.length === 0 || new Set(childIds).size !== childIds.length) {
    return {
      intent: "logBehaviour",
      types: await reloadTypes(),
      error: "Choose one or more children.",
    };
  }
  if (!typeId || (points !== null && !isWholeNumber(points))) {
    return {
      intent: "logBehaviour",
      types: await reloadTypes(),
      error: "Choose a good behaviour with zero or more points.",
    };
  }
  if (!requestId) {
    return {
      intent: "logBehaviour",
      types: await reloadTypes(),
      error:
        "Your browser couldn't prepare this request. Try again or use another browser.",
    };
  }

  const logged = await logGoodBehaviour({
    requestId,
    typeId,
    childIds,
    points,
  });
  return {
    intent: "logBehaviour",
    types: await reloadTypes(),
    affectedTypeId: typeId,
    logged,
  };
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

async function reloadTypes(): Promise<GoodBehaviourType[] | undefined> {
  try {
    return await getGoodBehaviourTypes();
  } catch {
    return undefined;
  }
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof GoodBehaviourApiError ? error.message : fallback;
}
