import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

type GeneratedType = components["schemas"]["GoodBehaviourTypeResponse"];

export interface GoodBehaviourType {
  id: string;
  name: string;
  description: string;
  points: number;
}

export interface GoodBehaviourTypeInput {
  name: string;
  description: string;
  points: number;
}

export interface LogGoodBehaviourInput {
  requestId: string;
  typeId: string;
  childIds: string[];
  points: number | null;
}

export interface LoggedGoodBehaviour {
  typeName: string;
  points: number;
  childIds: string[];
}

export class GoodBehaviourApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "GoodBehaviourApiError";
  }
}

export async function getGoodBehaviourTypes(): Promise<GoodBehaviourType[]> {
  const { data, error } = await apiClient().GET("/api/good-behaviour-types");
  if (!data) {
    throw new GoodBehaviourApiError(
      problemMessage(error, "We couldn't load the good behaviours."),
    );
  }

  return data.types.map(mapType);
}

export async function createGoodBehaviourType(
  input: GoodBehaviourTypeInput,
): Promise<GoodBehaviourType> {
  const { data, error } = await apiClient().POST("/api/good-behaviour-types", {
    body: input,
  });
  if (!data) {
    throw new GoodBehaviourApiError(
      problemMessage(error, "That good behaviour couldn't be created."),
    );
  }

  return mapType(data);
}

export async function updateGoodBehaviourType(
  id: string,
  input: GoodBehaviourTypeInput,
): Promise<GoodBehaviourType> {
  const { data, error } = await apiClient().PUT(
    "/api/good-behaviour-types/{id}",
    { params: { path: { id } }, body: input },
  );
  if (!data) {
    throw new GoodBehaviourApiError(
      problemMessage(error, "That good behaviour couldn't be saved."),
    );
  }

  return mapType(data);
}

export async function deleteGoodBehaviourType(id: string): Promise<void> {
  const { response, error } = await apiClient().DELETE(
    "/api/good-behaviour-types/{id}",
    { params: { path: { id } } },
  );
  if (!response.ok) {
    throw new GoodBehaviourApiError(
      problemMessage(error, "That good behaviour couldn't be deleted."),
    );
  }
}

export async function logGoodBehaviour(
  input: LogGoodBehaviourInput,
): Promise<LoggedGoodBehaviour> {
  const { data, error } = await apiClient().POST("/api/good-behaviours", {
    body: input,
  });
  if (!data) {
    throw new GoodBehaviourApiError(
      problemMessage(error, "That good behaviour couldn't be logged."),
    );
  }

  const first = data.awards[0]?.behaviour;
  if (!first) {
    throw new GoodBehaviourApiError(
      "The good behaviour response contained no awards.",
    );
  }

  return {
    typeName: first.typeName,
    points: Number(first.points),
    childIds: data.awards.map((award) => award.behaviour.childId),
  };
}

function apiClient() {
  return createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
}

function mapType(value: GeneratedType): GoodBehaviourType {
  return {
    id: value.id,
    name: value.name,
    description: value.description,
    points: Number(value.points),
  };
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
