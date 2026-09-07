import type { components } from "./schema";

type GeneratedAuthResponse = components["schemas"]["AuthResponse"];
type GeneratedAuthStart = components["schemas"]["AuthStartResponse"];
type GeneratedStartMember = components["schemas"]["StartMemberResponse"];
type GeneratedPinSetup = components["schemas"]["PinSetupResponse"];

export type MemberRole = "adult" | "child";

export interface AuthMember {
  id: string;
  displayName: string;
  role: MemberRole;
}

export interface AuthSession {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  member: AuthMember;
}

export interface StartMember extends AuthMember {
  requiresSurname: boolean;
}

export type AuthStart =
  | { state: "createFirstAdult" }
  | { state: "claimExistingAdult"; adults: StartMember[] }
  | { state: "signIn"; members: StartMember[] };

export interface PendingPinSetup {
  state: "setupPin";
  setupToken: string;
  expiresAtUtc: string;
  targetDisplayName: string;
  targetRole: MemberRole;
}

interface ProblemDetails {
  detail?: string;
  code?: string;
}

export class AuthApiError extends Error {
  public constructor(
    message: string,
    public readonly status: number,
    public readonly code?: string,
  ) {
    super(message);
    this.name = "AuthApiError";
  }
}

let session: AuthSession | null = null;
let pendingPinSetup: PendingPinSetup | null = null;

export function currentSession(): AuthSession | null {
  return session;
}

export function currentPinSetup(): PendingPinSetup | null {
  return pendingPinSetup;
}

export function acceptSession(value: AuthSession): void {
  session = value;
  pendingPinSetup = null;
}

export function clearIdentity(): void {
  session = null;
  pendingPinSetup = null;
}

export async function getAuthenticationStart(): Promise<AuthStart> {
  const response = await fetch("/api/auth/start", { credentials: "include" });
  const body = await readJson<GeneratedAuthStart>(response);
  if (body.state === "createFirstAdult") {
    return { state: "createFirstAdult" };
  }
  if (body.state === "claimExistingAdult") {
    return {
      state: "claimExistingAdult",
      adults: (body.adults ?? []).map(mapStartMember),
    };
  }
  if (body.state === "signIn") {
    return {
      state: "signIn",
      members: (body.members ?? []).map(mapStartMember),
    };
  }

  throw new AuthApiError("The sign-in state was not recognised.", 500);
}

export async function refreshSession(): Promise<AuthSession | null> {
  const response = await fetch("/api/auth/refresh", {
    method: "POST",
    credentials: "include",
  });
  if (!response.ok) {
    session = null;
    return null;
  }

  session = mapAuthResponse(await readJson<GeneratedAuthResponse>(response));
  return session;
}

export async function bootstrap(request: {
  mode: "createFirstAdult" | "claimExistingAdult";
  memberId: string | null;
  firstName: string | null;
  surname: string;
  pin: string;
}): Promise<AuthSession> {
  session = mapAuthResponse(
    await postJson<GeneratedAuthResponse>("/api/auth/bootstrap", request),
  );
  return session;
}

export async function signIn(
  memberId: string,
  pin: string,
): Promise<AuthSession> {
  session = mapAuthResponse(
    await postJson<GeneratedAuthResponse>("/api/auth/sign-in", {
      memberId,
      pin,
    }),
  );
  return session;
}

export async function logout(): Promise<void> {
  try {
    await fetch("/api/auth/logout", {
      method: "POST",
      credentials: "include",
      headers: authorizationHeaders(),
    });
  } finally {
    clearIdentity();
  }
}

export async function beginPinSetup(
  memberId: string,
  surname: string | null,
): Promise<PendingPinSetup> {
  const body = await postJson<GeneratedPinSetup>(
    `/api/users/${encodeURIComponent(memberId)}/pin-setup`,
    { surname },
    true,
  );
  pendingPinSetup = {
    state: "setupPin",
    setupToken: body.setupToken,
    expiresAtUtc: body.expiresAtUtc,
    targetDisplayName: body.targetDisplayName,
    targetRole: mapRole(body.targetRole),
  };
  session = null;
  return pendingPinSetup;
}

export async function setupPin(pin: string): Promise<AuthSession> {
  if (!pendingPinSetup) {
    throw new AuthApiError(
      "This PIN setup has expired. Ask a grown-up to start again.",
      400,
    );
  }

  session = mapAuthResponse(
    await postJson<GeneratedAuthResponse>("/api/auth/setup-pin", {
      setupToken: pendingPinSetup.setupToken,
      pin,
    }),
  );
  pendingPinSetup = null;
  return session;
}

export async function authenticatedFetch(
  input: RequestInfo | URL,
  init: RequestInit = {},
): Promise<Response> {
  if (!session || isNearExpiry(session.accessTokenExpiresAtUtc)) {
    await refreshSession();
  }
  if (!session) {
    throw new AuthApiError(
      "Your session has expired. Sign in again.",
      401,
      "session_expired",
    );
  }

  const headers = new Headers(init.headers);
  headers.set("Authorization", `Bearer ${session.accessToken}`);
  const response = await fetch(input, {
    ...init,
    credentials: "include",
    headers,
  });
  if (response.status === 401) {
    session = null;
  }
  return response;
}

function authorizationHeaders(): Headers {
  const headers = new Headers();
  if (session) {
    headers.set("Authorization", `Bearer ${session.accessToken}`);
  }
  return headers;
}

async function postJson<T>(
  url: string,
  body: unknown,
  authorized = false,
): Promise<T> {
  const headers = authorizationHeaders();
  headers.set("Content-Type", "application/json");
  return readJson<T>(
    await fetch(url, {
      method: "POST",
      credentials: "include",
      headers: authorized ? headers : { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    }),
  );
}

async function readJson<T>(response: Response): Promise<T> {
  if (response.ok) {
    return (await response.json()) as T;
  }

  let problem: ProblemDetails = {};
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // A stable fallback is safer than exposing an upstream response body.
  }
  throw new AuthApiError(
    problem.detail ?? "That request could not be completed.",
    response.status,
    problem.code,
  );
}

function mapAuthResponse(value: GeneratedAuthResponse): AuthSession {
  return {
    accessToken: value.accessToken,
    accessTokenExpiresAtUtc: value.accessTokenExpiresAtUtc,
    member: {
      id: value.member.id,
      displayName: value.member.displayName,
      role: mapRole(value.member.role),
    },
  };
}

function mapStartMember(value: GeneratedStartMember): StartMember {
  return {
    id: value.id,
    displayName: value.displayName,
    role: mapRole(value.role),
    requiresSurname: value.requiresSurname,
  };
}

function mapRole(value: string): MemberRole {
  if (value === "adult" || value === "child") {
    return value;
  }
  throw new AuthApiError("The member role was not recognised.", 500);
}

function isNearExpiry(expiresAtUtc: string): boolean {
  return Date.parse(expiresAtUtc) <= Date.now() + 15_000;
}
