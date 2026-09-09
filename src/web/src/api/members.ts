import createClient from "openapi-fetch";

import type { components, paths } from "./schema";
import { authenticatedFetch } from "./auth";

type GeneratedFamilyMember = components["schemas"]["FamilyMemberResponse"];
type CreateFamilyMemberRequest =
  components["schemas"]["CreateFamilyMemberRequest"];
type UpdateFamilyMemberRequest =
  components["schemas"]["UpdateFamilyMemberRequest"];

export type FamilyMemberRole = "adult" | "child";

export interface FamilyMember {
  id: string;
  firstName: string;
  surname: string | null;
  nickname: string | null;
  displayName: string;
  role: FamilyMemberRole;
  isCredentialReady: boolean;
  isActive: boolean;
}

export class FamilyMemberApiError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "FamilyMemberApiError";
  }
}

export async function getFamilyMembers(
  includeInactive = false,
): Promise<FamilyMember[]> {
  const { data, error } = await apiClient().GET("/api/users", {
    params: { query: { includeInactive } },
  });
  if (!data) {
    throw new FamilyMemberApiError(
      problemMessage(error, "We couldn't load the family members."),
    );
  }

  return data.map(mapMember);
}

export async function createFamilyMember(
  request: CreateFamilyMemberRequest,
): Promise<FamilyMember> {
  const { data, error } = await apiClient().POST("/api/users", {
    body: request,
  });
  if (!data) {
    throw new FamilyMemberApiError(
      problemMessage(error, "That family member couldn't be created."),
    );
  }

  return mapMember(data);
}

export async function updateFamilyMember(
  memberId: string,
  request: UpdateFamilyMemberRequest,
): Promise<FamilyMember> {
  const { data, error } = await apiClient().PATCH("/api/users/{memberId}", {
    params: { path: { memberId } },
    body: request,
  });
  if (!data) {
    throw new FamilyMemberApiError(
      problemMessage(error, "That family member couldn't be updated."),
    );
  }

  return mapMember(data);
}

export async function deactivateFamilyMember(
  memberId: string,
): Promise<FamilyMember> {
  const { data, error } = await apiClient().DELETE("/api/users/{memberId}", {
    params: { path: { memberId } },
  });
  if (!data) {
    throw new FamilyMemberApiError(
      problemMessage(error, "That family member couldn't be deactivated."),
    );
  }

  return mapMember(data);
}

export async function restoreFamilyMember(
  memberId: string,
): Promise<FamilyMember> {
  const { data, error } = await apiClient().POST(
    "/api/users/{memberId}/restore",
    { params: { path: { memberId } } },
  );
  if (!data) {
    throw new FamilyMemberApiError(
      problemMessage(error, "That family member couldn't be restored."),
    );
  }

  return mapMember(data);
}

function apiClient() {
  return createClient<paths>({
    baseUrl: window.location.origin,
    fetch: authenticatedFetch,
  });
}

function mapMember(value: GeneratedFamilyMember): FamilyMember {
  if (value.role !== "adult" && value.role !== "child") {
    throw new FamilyMemberApiError(
      "The family member role was not recognised.",
    );
  }

  return {
    id: value.id,
    firstName: value.firstName,
    surname: value.surname ?? null,
    nickname: value.nickname ?? null,
    displayName: value.displayName,
    role: value.role,
    isCredentialReady: value.isCredentialReady,
    isActive: value.isActive,
  };
}

function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === "object" && "detail" in error) {
    const detail = (error as { detail?: unknown }).detail;
    if (typeof detail === "string" && detail.length > 0) {
      return detail;
    }
  }

  return fallback;
}
