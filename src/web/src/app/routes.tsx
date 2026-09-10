import { useLoaderData } from "react-router";
import type { ActionFunctionArgs } from "react-router";
import type { RouteObject } from "react-router";

import {
  AuthApiError,
  beginPinReset,
  beginPinSetup,
  bootstrap,
  clearIdentity,
  currentPinSetup,
  currentSession,
  getAuthenticationStart,
  logout,
  refreshSession,
  setupPin,
  signIn,
} from "../api/auth";
import type { AuthStart, PendingPinSetup } from "../api/auth";
import {
  ApiError,
  addJob,
  approveJob,
  completeJob,
  createDailyRecurringJob,
  createMonthlyRecurringJob,
  createWeeklyRecurringJob,
  getToday,
  rejectJob,
} from "../api/today";
import { LoadingPage } from "./LoadingPage";
import { AuthPage } from "../features/identity/AuthPage";
import { TodayPage } from "../features/today/TodayPage";
import type { TodayBoard } from "../api/today";
import {
  createFamilyMember,
  deactivateFamilyMember,
  FamilyMemberApiError,
  getFamilyMembers,
  restoreFamilyMember,
  updateFamilyMember,
} from "../api/members";
import type { FamilyMember } from "../api/members";

export interface CompleteActionResult {
  intent: "complete";
  jobId: string;
  error?: string;
}

export interface AddJobActionResult {
  intent: "add";
  success?: boolean;
  error?: string;
}

export interface ApproveActionResult {
  intent: "approve";
  jobId: string;
  error?: string;
}

export interface AddRecurringJobActionResult {
  intent: "addRecurring";
  frequency?: "daily" | "weekly" | "monthly";
  success?: boolean;
  generatedThrough?: string;
  error?: string;
}

export interface RejectActionResult {
  intent: "reject";
  jobId: string;
  error?: string;
}

export type TodayActionResult =
  | CompleteActionResult
  | AddJobActionResult
  | AddRecurringJobActionResult
  | ApproveActionResult
  | RejectActionResult;

export interface IdentityActionResult {
  intent:
    | "bootstrap"
    | "signIn"
    | "logout"
    | "beginPinSetup"
    | "beginPinReset"
    | "setupPin";
  error?: string;
}

export interface FamilyMembersActionResult {
  intent?:
    "createMember" | "updateMember" | "deactivateMember" | "restoreMember";
  members?: FamilyMember[] | undefined;
  affectedMemberId?: string | undefined;
  error?: string;
}

export type AppActionResult = TodayActionResult | IdentityActionResult;

export type AppLoaderData =
  AuthStart | PendingPinSetup | { state: "authenticated"; board: TodayBoard };

async function appLoader(): Promise<AppLoaderData> {
  const setup = currentPinSetup();
  if (setup) {
    return setup;
  }

  const active = currentSession() ?? (await refreshSession());
  if (active) {
    try {
      return { state: "authenticated", board: await getToday() };
    } catch (error) {
      if (!(error instanceof AuthApiError) || error.status !== 401) {
        throw error;
      }
      clearIdentity();
    }
  }

  return getAuthenticationStart();
}

async function todayAction({
  request,
}: ActionFunctionArgs): Promise<AppActionResult> {
  const form = await request.formData();
  if (form.get("intent") === "bootstrap") {
    return bootstrapAction(form);
  }
  if (form.get("intent") === "signIn") {
    return signInAction(form);
  }
  if (form.get("intent") === "logout") {
    await logout();
    return { intent: "logout" };
  }
  if (form.get("intent") === "beginPinSetup") {
    return beginPinSetupAction(form);
  }
  if (form.get("intent") === "beginPinReset") {
    return beginPinResetAction(form);
  }
  if (form.get("intent") === "setupPin") {
    return setupPinAction(form);
  }
  if (form.get("intent") === "add") {
    return addJobAction(form);
  }
  if (form.get("intent") === "addRecurring") {
    return addRecurringJobAction(form);
  }
  if (form.get("intent") === "approve") {
    return approveAction(form);
  }
  if (form.get("intent") === "reject") {
    return rejectAction(form);
  }

  return completeAction(form);
}

async function familyLoader(): Promise<FamilyMembersActionResult> {
  try {
    return { members: await getFamilyMembers(true) };
  } catch (error) {
    return {
      error:
        error instanceof FamilyMemberApiError
          ? error.message
          : "We couldn't load the family members.",
    };
  }
}

async function familyAction({
  request,
}: ActionFunctionArgs): Promise<FamilyMembersActionResult> {
  const form = await request.formData();
  const intent = form.get("intent");
  if (
    intent !== "createMember" &&
    intent !== "updateMember" &&
    intent !== "deactivateMember" &&
    intent !== "restoreMember"
  ) {
    return { error: "That family action wasn't recognised." };
  }

  const firstName = form.get("firstName");
  const surname = form.get("surname");
  const nickname = form.get("nickname");
  const submittedMemberId = form.get("memberId");
  const memberId =
    typeof submittedMemberId === "string" && submittedMemberId.length > 0
      ? submittedMemberId
      : undefined;
  const names = normalizeMemberNames(firstName, surname, nickname);
  const needsNames = intent === "createMember" || intent === "updateMember";
  if (needsNames && names === null) {
    return {
      intent,
      affectedMemberId: memberId,
      members: await loadFamilyMembersAfterAction(),
      error: "Check the family member details.",
    };
  }

  try {
    let affected: FamilyMember;
    if (intent === "createMember") {
      const role = form.get("role");
      if (names === null || (role !== "adult" && role !== "child")) {
        return {
          intent,
          members: await loadFamilyMembersAfterAction(),
          error: "Check the family member details.",
        };
      }

      affected = await createFamilyMember({
        firstName: names.firstName,
        surname: names.surname,
        nickname: names.nickname,
        role,
      });
    } else {
      if (memberId === undefined) {
        return { intent, error: "Choose a family member." };
      }

      if (intent === "updateMember") {
        if (names === null) {
          return {
            intent,
            members: await loadFamilyMembersAfterAction(),
            error: "Check the family member details.",
          };
        }

        affected = await updateFamilyMember(memberId, names);
      } else if (intent === "deactivateMember") {
        affected = await deactivateFamilyMember(memberId);
      } else {
        affected = await restoreFamilyMember(memberId);
      }
    }

    return {
      intent,
      members: await getFamilyMembers(true),
      affectedMemberId: affected.id,
    };
  } catch (error) {
    return {
      intent,
      affectedMemberId: memberId,
      members: await loadFamilyMembersAfterAction(),
      error:
        error instanceof FamilyMemberApiError
          ? error.message
          : "That family change couldn't be saved.",
    };
  }
}

function normalizeMemberNames(
  firstName: FormDataEntryValue | null,
  surname: FormDataEntryValue | null,
  nickname: FormDataEntryValue | null,
): { firstName: string; surname: string; nickname: string | null } | null {
  if (
    typeof firstName !== "string" ||
    firstName.trim().length === 0 ||
    firstName.trim().length > 100 ||
    typeof surname !== "string" ||
    surname.trim().length === 0 ||
    surname.trim().length > 100 ||
    typeof nickname !== "string" ||
    nickname.trim().length > 100
  ) {
    return null;
  }

  return {
    firstName: firstName.trim(),
    surname: surname.trim(),
    nickname: nickname.trim() || null,
  };
}

async function loadFamilyMembersAfterAction(): Promise<
  FamilyMember[] | undefined
> {
  try {
    return await getFamilyMembers(true);
  } catch {
    return undefined;
  }
}

async function bootstrapAction(form: FormData): Promise<IdentityActionResult> {
  const mode = form.get("mode");
  const memberId = form.get("memberId");
  const firstName = form.get("firstName");
  const surname = form.get("surname");
  const pin = form.get("pin");
  const confirmation = form.get("pinConfirmation");
  if (
    (mode !== "createFirstAdult" && mode !== "claimExistingAdult") ||
    typeof surname !== "string" ||
    surname.trim().length === 0 ||
    typeof pin !== "string" ||
    !/^\d{6}$/.test(pin) ||
    pin !== confirmation ||
    (mode === "createFirstAdult" &&
      (typeof firstName !== "string" || firstName.trim().length === 0)) ||
    (mode === "claimExistingAdult" &&
      (typeof memberId !== "string" || memberId.length === 0))
  ) {
    return {
      intent: "bootstrap",
      error: "Check the names and matching six-digit PIN.",
    };
  }

  try {
    await bootstrap({
      mode,
      memberId: mode === "claimExistingAdult" ? String(memberId) : null,
      firstName: mode === "createFirstAdult" ? String(firstName).trim() : null,
      surname: surname.trim(),
      pin,
    });
    return { intent: "bootstrap" };
  } catch (error) {
    return identityError(
      "The household could not be set up.",
      "bootstrap",
      error,
    );
  }
}

async function signInAction(form: FormData): Promise<IdentityActionResult> {
  const memberId = form.get("memberId");
  const pin = form.get("pin");
  const pinLength = form.get("role") === "adult" ? 6 : 4;
  if (
    typeof memberId !== "string" ||
    typeof pin !== "string" ||
    !new RegExp(`^\\d{${pinLength}}$`).test(pin)
  ) {
    return {
      intent: "signIn",
      error: "Choose your profile and enter your PIN.",
    };
  }

  try {
    await signIn(memberId, pin);
    return { intent: "signIn" };
  } catch (error) {
    return identityError(
      "The profile or PIN was not accepted.",
      "signIn",
      error,
    );
  }
}

async function beginPinSetupAction(
  form: FormData,
): Promise<IdentityActionResult> {
  const memberId = form.get("memberId");
  const surname = form.get("surname");
  if (typeof memberId !== "string" || memberId.length === 0) {
    return {
      intent: "beginPinSetup",
      error: "Choose who will use the board next.",
    };
  }

  try {
    await beginPinSetup(
      memberId,
      typeof surname === "string" && surname.trim().length > 0
        ? surname.trim()
        : null,
    );
    return { intent: "beginPinSetup" };
  } catch (error) {
    return identityError(
      "PIN setup could not be started.",
      "beginPinSetup",
      error,
    );
  }
}

async function setupPinAction(form: FormData): Promise<IdentityActionResult> {
  const pin = form.get("pin");
  const confirmation = form.get("pinConfirmation");
  const pinLength = form.get("role") === "adult" ? 6 : 4;
  if (
    typeof pin !== "string" ||
    !new RegExp(`^\\d{${pinLength}}$`).test(pin) ||
    pin !== confirmation
  ) {
    return {
      intent: "setupPin",
      error: `Enter a matching ${pinLength}-digit PIN.`,
    };
  }

  try {
    await setupPin(pin);
    return { intent: "setupPin" };
  } catch (error) {
    return identityError(
      "This PIN setup has expired. Ask a grown-up to start again.",
      "setupPin",
      error,
    );
  }
}

async function beginPinResetAction(
  form: FormData,
): Promise<IdentityActionResult> {
  const memberId = form.get("memberId");
  if (typeof memberId !== "string" || memberId.length === 0) {
    return {
      intent: "beginPinReset",
      error: "Choose whose PIN should be reset.",
    };
  }

  try {
    await beginPinReset(memberId);
    return { intent: "beginPinReset" };
  } catch (error) {
    return identityError("The PIN could not be reset.", "beginPinReset", error);
  }
}

function identityError(
  fallback: string,
  intent: IdentityActionResult["intent"],
  error: unknown,
): IdentityActionResult {
  return {
    intent,
    error: error instanceof AuthApiError ? error.message : fallback,
  };
}

async function addRecurringJobAction(
  form: FormData,
): Promise<AddRecurringJobActionResult> {
  const submittedRequestId = form.get("requestId");
  const requestId =
    typeof submittedRequestId === "string" && submittedRequestId.length > 0
      ? submittedRequestId
      : crypto.randomUUID();
  const recurrenceFrequency = form.get("recurrenceFrequency");
  const childIds = form
    .getAll("childIds")
    .filter((value): value is string => typeof value === "string");
  const name = form.get("name");
  const description = form.get("description");
  const pointsValue = form.get("points");
  const agendaPeriod = form.get("agendaPeriod");
  const scheduledTime = form.get("scheduledTime");
  const startDate = form.get("startDate");
  const endDate = form.get("endDate");
  const weekdays = form
    .getAll("weekdays")
    .filter((value): value is string => typeof value === "string");
  const dayOfMonthValue = form.get("dayOfMonth");
  const dayOfMonth = Number(dayOfMonthValue);
  const points = Number(pointsValue);
  const validAgendaPeriods = [
    "morning",
    "arrivingHome",
    "evening",
    "unscheduled",
  ] as const;

  if (
    (recurrenceFrequency !== "daily" &&
      recurrenceFrequency !== "weekly" &&
      recurrenceFrequency !== "monthly") ||
    childIds.length === 0 ||
    new Set(childIds).size !== childIds.length ||
    typeof name !== "string" ||
    name.trim().length === 0 ||
    name.trim().length > 160 ||
    typeof description !== "string" ||
    description.trim().length > 1000 ||
    typeof pointsValue !== "string" ||
    !Number.isInteger(points) ||
    points < 0 ||
    typeof agendaPeriod !== "string" ||
    !validAgendaPeriods.some((value) => value === agendaPeriod) ||
    typeof startDate !== "string" ||
    startDate.length === 0 ||
    (typeof endDate === "string" &&
      endDate.length > 0 &&
      endDate < startDate) ||
    (recurrenceFrequency === "weekly" && weekdays.length === 0) ||
    (recurrenceFrequency === "monthly" &&
      (typeof dayOfMonthValue !== "string" ||
        !Number.isInteger(dayOfMonth) ||
        dayOfMonth < 1 ||
        dayOfMonth > 31))
  ) {
    return {
      intent: "addRecurring",
      error: "Check the recurring job details and try again.",
    };
  }

  try {
    const recurringRequest = {
      requestId,
      childIds,
      name,
      description,
      points,
      agendaPeriod: agendaPeriod as (typeof validAgendaPeriods)[number],
      scheduledTime:
        typeof scheduledTime === "string" && scheduledTime.length > 0
          ? scheduledTime
          : null,
      startDate,
      endDate:
        typeof endDate === "string" && endDate.length > 0 ? endDate : null,
    };
    let result;
    if (recurrenceFrequency === "monthly") {
      result = await createMonthlyRecurringJob({
        ...recurringRequest,
        dayOfMonth,
      });
    } else if (recurrenceFrequency === "weekly") {
      result = await createWeeklyRecurringJob({
        ...recurringRequest,
        weekdays,
      });
    } else {
      result = await createDailyRecurringJob(recurringRequest);
    }
    const generatedThrough = result.assignments[0]?.generatedThrough;
    if (!generatedThrough) {
      throw new Error("The recurring job response contained no assignments.");
    }
    return {
      intent: "addRecurring",
      frequency: recurrenceFrequency,
      success: true,
      generatedThrough,
    };
  } catch (error) {
    return {
      intent: "addRecurring",
      error:
        error instanceof ApiError
          ? error.message
          : "That recurring job couldn't be created.",
    };
  }
}

async function rejectAction(form: FormData): Promise<RejectActionResult> {
  const jobId = form.get("jobId");
  const reason = form.get("reason");
  if (typeof jobId !== "string") {
    return {
      intent: "reject",
      jobId: "",
      error: "The selected job was missing.",
    };
  }

  try {
    await rejectJob(jobId, typeof reason === "string" ? reason : null);
    return { intent: "reject", jobId };
  } catch (error) {
    return {
      intent: "reject",
      jobId,
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be rejected.",
    };
  }
}

async function approveAction(form: FormData): Promise<ApproveActionResult> {
  const jobId = form.get("jobId");
  if (typeof jobId !== "string") {
    return {
      intent: "approve",
      jobId: "",
      error: "The selected job was missing.",
    };
  }

  try {
    await approveJob(jobId);
    return { intent: "approve", jobId };
  } catch (error) {
    return {
      intent: "approve",
      jobId,
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be approved.",
    };
  }
}

async function completeAction(form: FormData): Promise<CompleteActionResult> {
  const jobId = form.get("jobId");
  if (typeof jobId !== "string") {
    return {
      intent: "complete",
      jobId: "",
      error: "The selected job was missing.",
    };
  }

  try {
    await completeJob(jobId);
    return { intent: "complete", jobId };
  } catch (error) {
    return {
      intent: "complete",
      jobId,
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be completed.",
    };
  }
}

async function addJobAction(form: FormData): Promise<AddJobActionResult> {
  const name = form.get("name");
  const description = form.get("description");
  const pointsValue = form.get("points");
  const childIds = form
    .getAll("childIds")
    .filter((value): value is string => typeof value === "string");
  const points = Number(pointsValue);

  if (childIds.length === 0 || new Set(childIds).size !== childIds.length) {
    return { intent: "add", error: "Choose one or more children." };
  }

  if (typeof name !== "string" || name.trim().length === 0) {
    return { intent: "add", error: "Enter a job name." };
  }
  if (name.trim().length > 160) {
    return {
      intent: "add",
      error: "The job name must be 160 characters or fewer.",
    };
  }
  if (typeof description !== "string" || description.trim().length > 1000) {
    return {
      intent: "add",
      error: "The description must be 1000 characters or fewer.",
    };
  }
  if (
    typeof pointsValue !== "string" ||
    pointsValue.trim().length === 0 ||
    !Number.isInteger(points) ||
    points < 0
  ) {
    return { intent: "add", error: "Enter zero or more whole points." };
  }

  try {
    await addJob({ childIds, name, description, points });
    return { intent: "add", success: true };
  } catch (error) {
    return {
      intent: "add",
      error:
        error instanceof ApiError
          ? error.message
          : "That job couldn't be added.",
    };
  }
}

export const routes: RouteObject[] = [
  {
    path: "/",
    loader: appLoader,
    action: todayAction,
    Component: AppPage,
    HydrateFallback: LoadingPage,
    shouldRevalidate: ({ actionResult, defaultShouldRevalidate }) => {
      if (
        actionResult &&
        typeof actionResult === "object" &&
        "error" in actionResult &&
        actionResult.error
      ) {
        return false;
      }

      return defaultShouldRevalidate;
    },
  },
  {
    path: "/family",
    loader: familyLoader,
    action: familyAction,
  },
];

function AppPage() {
  const data = useLoaderData() as AppLoaderData;
  return data.state === "authenticated" ? (
    <TodayPage board={data.board} />
  ) : (
    <AuthPage state={data} />
  );
}
