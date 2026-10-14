import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type {
  AppActionResult,
  FamilyMembersActionResult,
  FamilyMembersLoaderData,
} from "../../app/routes";
import type { FamilyMember } from "../../api/members";
import { formText } from "../../app/actionDescriptions";
import { useUndoQueue } from "../../app/undoQueue";
import type { QueuedAction } from "../../app/undoQueue";

const memberIntents = new Set([
  "createMember",
  "updateMember",
  "deactivateMember",
  "restoreMember",
]);

export function FamilyMembersPanel({
  currentMemberId,
}: {
  currentMemberId: string;
}) {
  const family = useFetcher<FamilyMembersLoaderData>();
  const { actions } = useUndoQueue();
  const members = showQueuedMemberChanges(family.data?.members ?? [], actions);
  const hasLoadedMembers = family.data?.members !== undefined;
  const active = members.filter(({ member }) => member.isActive);
  const inactive = members.filter(({ member }) => !member.isActive);

  return (
    <details
      className="grown-up-tools family-members"
      onToggle={(event) => {
        if (
          event.currentTarget.open &&
          family.state === "idle" &&
          family.data === undefined
        ) {
          void family.load("/family");
        }
      }}
    >
      <summary>
        <span className="eyebrow">Family administration</span>
        <span className="grown-up-tools__action">
          Manage family <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <div className="family-members__intro">
          <div>
            <h2>Family members</h2>
            <p>
              Add a child or grown-up, then hand over privately when they are
              ready to choose their PIN.
            </p>
          </div>
          {hasLoadedMembers ? <CreateMemberForm /> : null}
        </div>

        {!hasLoadedMembers && family.data?.error ? (
          <div className="family-members__load-error">
            <p className="error-message" role="alert">
              {family.data.error}
            </p>
            <button type="button" onClick={() => void family.load("/family")}>
              Try again
            </button>
          </div>
        ) : !hasLoadedMembers ? (
          <p role="status">Loading family members…</p>
        ) : (
          <>
            <ul className="family-member-list" aria-label="Family members">
              {active.map(({ member, queued }) => (
                <FamilyMemberCard
                  key={`${member.id}-${member.isActive}`}
                  member={member}
                  currentMemberId={currentMemberId}
                  queued={queued}
                />
              ))}
              {actions
                .filter(
                  (action) => action.formData.get("intent") === "createMember",
                )
                .map((action) => (
                  <AddingMemberCard key={action.id} form={action.formData} />
                ))}
            </ul>
            <section
              className="inactive-family-members"
              aria-labelledby="inactive-family-heading"
            >
              <div>
                <h3 id="inactive-family-heading">Inactive profiles</h3>
                <p>These profiles are hidden from sign-in and assignments.</p>
              </div>
              {inactive.length > 0 ? (
                <ul
                  className="family-member-list family-member-list--inactive"
                  aria-label="Inactive family members"
                >
                  {inactive.map(({ member, queued }) => (
                    <FamilyMemberCard
                      key={`${member.id}-${member.isActive}`}
                      member={member}
                      currentMemberId={currentMemberId}
                      queued={queued}
                    />
                  ))}
                </ul>
              ) : (
                <p className="inactive-family-members__empty">
                  No inactive profiles.
                </p>
              )}
            </section>
          </>
        )}
      </div>
    </details>
  );
}

/** Members as they will be once queued profile changes are saved. */
function showQueuedMemberChanges(
  members: FamilyMember[],
  actions: readonly QueuedAction[],
): { member: FamilyMember; queued: boolean }[] {
  return members.map((member) => {
    const form = actions.find(
      (action) =>
        memberIntents.has(String(action.formData.get("intent"))) &&
        action.formData.get("memberId") === member.id,
    )?.formData;
    switch (form?.get("intent")) {
      case "deactivateMember":
        return { member: { ...member, isActive: false }, queued: true };
      case "restoreMember":
        return { member: { ...member, isActive: true }, queued: true };
      case "updateMember": {
        const firstName = formText(form!, "firstName").trim();
        const nickname = formText(form!, "nickname").trim() || null;
        return {
          member: {
            ...member,
            firstName,
            surname: formText(form!, "surname").trim(),
            nickname,
            displayName: nickname ?? firstName,
          },
          queued: true,
        };
      }
      default:
        return { member, queued: false };
    }
  });
}

function AddingMemberCard({ form }: { form: FormData }) {
  const firstName = formText(form, "firstName").trim();
  const nickname = formText(form, "nickname").trim();
  return (
    <li className="family-member--queued">
      <div className="family-member-list__identity">
        <strong>{nickname || firstName}</strong>
        <span>
          {firstName} {formText(form, "surname").trim()}
        </span>
      </div>
      <div className="family-member-list__status">
        <span>{form.get("role") === "adult" ? "Grown-up" : "Child"}</span>
        <span className="status--not-set">Being added</span>
      </div>
    </li>
  );
}

function CreateMemberForm() {
  const fetcher = useFetcher<FamilyMembersActionResult>();
  const formRef = useRef<HTMLFormElement>(null);
  const error =
    fetcher.data?.intent === "createMember" ? fetcher.data.error : undefined;
  const submitting = fetcher.state === "submitting";

  useEffect(() => {
    if (fetcher.state === "idle" && fetcher.data?.queued) {
      formRef.current?.reset();
    }
  }, [fetcher.data, fetcher.state]);

  return (
    <fetcher.Form
      method="post"
      action="/family"
      className="family-member-form"
      ref={formRef}
    >
      <input type="hidden" name="intent" value="createMember" />
      <div className="form-group">
        <label htmlFor="member-first-name">First name</label>
        <input
          id="member-first-name"
          name="firstName"
          maxLength={100}
          required
        />
      </div>
      <div className="form-group">
        <label htmlFor="member-surname">Surname</label>
        <input id="member-surname" name="surname" maxLength={100} required />
      </div>
      <div className="form-group">
        <label htmlFor="member-nickname">Nickname (optional)</label>
        <input id="member-nickname" name="nickname" maxLength={100} />
      </div>
      <div className="form-group">
        <label htmlFor="member-role">Role</label>
        <select id="member-role" name="role" defaultValue="child" required>
          <option value="child">Child</option>
          <option value="adult">Grown-up</option>
        </select>
      </div>
      {error ? (
        <p className="error-message" role="alert">
          {error}
        </p>
      ) : null}
      <button type="submit" disabled={submitting}>
        {submitting ? "Adding…" : "Add family member"}
      </button>
    </fetcher.Form>
  );
}

function FamilyMemberCard({
  member,
  currentMemberId,
  queued,
}: {
  member: FamilyMember;
  currentMemberId: string;
  queued: boolean;
}) {
  const fetcher = useFetcher<FamilyMembersActionResult>();
  const handoff = useFetcher<AppActionResult>();
  const editDetailsRef = useRef<HTMLDetailsElement>(null);
  const editFormRef = useRef<HTMLFormElement>(null);
  const [confirmingDeactivation, setConfirmingDeactivation] = useState(false);
  const [confirmingPinReset, setConfirmingPinReset] = useState(false);
  const handoffError =
    handoff.data?.intent === "beginPinSetup" ||
    handoff.data?.intent === "beginPinReset"
      ? handoff.data.error
      : undefined;
  const memberAction = fetcher.data;
  const mutationError = memberAction?.error;
  // A queued change can still be undone; until then the profile is locked.
  const submitting = fetcher.state !== "idle" || queued;
  const fullName = [member.firstName, member.surname].filter(Boolean).join(" ");

  useEffect(() => {
    if (
      fetcher.state === "idle" &&
      memberAction?.intent === "updateMember" &&
      memberAction.queued
    ) {
      editFormRef.current?.reset();
      if (editDetailsRef.current) {
        editDetailsRef.current.open = false;
      }
    }
  }, [fetcher.state, memberAction]);

  return (
    <li className={member.isActive ? undefined : "family-member--inactive"}>
      <div className="family-member-list__identity">
        <strong>{member.displayName}</strong>
        <span>{fullName}</span>
      </div>
      <div className="family-member-list__status">
        <span>{member.role === "adult" ? "Grown-up" : "Child"}</span>
        <span
          className={
            member.isCredentialReady ? "status--ready" : "status--not-set"
          }
        >
          {member.isCredentialReady ? "PIN ready" : "PIN not set"}
        </span>
        {!member.isActive ? (
          <span className="status--inactive">Inactive</span>
        ) : null}
      </div>
      {member.isActive && !member.isCredentialReady ? (
        <handoff.Form method="post" action="/" className="member-handoff-form">
          <input type="hidden" name="intent" value="beginPinSetup" />
          <input type="hidden" name="memberId" value={member.id} />
          {member.surname === null ? (
            <label>
              <span>Surname</span>
              <input name="surname" maxLength={100} required />
            </label>
          ) : null}
          {handoffError ? (
            <p className="error-message" role="alert">
              {handoffError}
            </p>
          ) : null}
          <button type="submit" disabled={handoff.state !== "idle"}>
            {handoff.state !== "idle" ? "Starting…" : "Set up PIN now"}
          </button>
        </handoff.Form>
      ) : null}
      {member.isActive &&
      member.isCredentialReady &&
      member.id !== currentMemberId ? (
        confirmingPinReset ? (
          <div
            className="deactivate-confirmation pin-reset-confirmation"
            role="group"
            aria-label={`Reset ${member.displayName}'s PIN`}
          >
            <p>
              Reset {member.displayName}&apos;s PIN? They will be signed out
              everywhere. Hand the screen to them so only they see and choose
              the replacement PIN.
            </p>
            {handoffError ? (
              <p className="error-message" role="alert">
                {handoffError}
              </p>
            ) : null}
            <div>
              <handoff.Form method="post" action="/">
                <input type="hidden" name="intent" value="beginPinReset" />
                <input type="hidden" name="memberId" value={member.id} />
                <button
                  type="submit"
                  className="button--danger"
                  disabled={handoff.state !== "idle"}
                >
                  {handoff.state !== "idle"
                    ? "Resetting…"
                    : "Yes, reset and hand over"}
                </button>
              </handoff.Form>
              <button
                type="button"
                className="button--quiet"
                onClick={() => setConfirmingPinReset(false)}
                disabled={handoff.state !== "idle"}
              >
                Keep current PIN
              </button>
            </div>
          </div>
        ) : (
          <button
            type="button"
            className="button--quiet family-member-pin-reset"
            onClick={() => setConfirmingPinReset(true)}
          >
            Reset PIN
          </button>
        )
      ) : null}
      <details className="family-member-edit" ref={editDetailsRef}>
        <summary>Edit profile</summary>
        <fetcher.Form method="post" action="/family" ref={editFormRef}>
          <input type="hidden" name="intent" value="updateMember" />
          <input type="hidden" name="memberId" value={member.id} />
          <input type="hidden" name="memberName" value={member.displayName} />
          <label>
            <span>First name</span>
            <input
              name="firstName"
              defaultValue={member.firstName}
              maxLength={100}
              required
            />
          </label>
          <label>
            <span>Surname</span>
            <input
              name="surname"
              defaultValue={member.surname ?? ""}
              maxLength={100}
              required
            />
          </label>
          <label>
            <span>Nickname (optional)</span>
            <input
              name="nickname"
              defaultValue={member.nickname ?? ""}
              maxLength={100}
            />
          </label>
          {memberAction?.intent === "updateMember" && mutationError ? (
            <p className="error-message" role="alert">
              {mutationError}
            </p>
          ) : null}
          <button type="submit" disabled={submitting}>
            {submitting ? "Saving…" : "Save profile"}
          </button>
        </fetcher.Form>
      </details>
      {member.isActive ? (
        member.id === currentMemberId ? (
          <p className="family-member-list__self">Signed-in profile</p>
        ) : confirmingDeactivation ? (
          <div
            className="deactivate-confirmation"
            role="group"
            aria-label={`Deactivate ${member.displayName}`}
          >
            <p>
              Deactivate {member.displayName}? They will be signed out and
              hidden from new assignments. Their history stays safe.
            </p>
            {memberAction?.intent === "deactivateMember" && mutationError ? (
              <p className="error-message" role="alert">
                {mutationError}
              </p>
            ) : null}
            <div>
              <fetcher.Form method="post" action="/family">
                <input type="hidden" name="intent" value="deactivateMember" />
                <input type="hidden" name="memberId" value={member.id} />
                <input
                  type="hidden"
                  name="memberName"
                  value={member.displayName}
                />
                <button
                  type="submit"
                  className="button--danger"
                  disabled={submitting}
                >
                  {submitting ? "Deactivating…" : "Yes, deactivate"}
                </button>
              </fetcher.Form>
              <button
                type="button"
                className="button--quiet"
                onClick={() => setConfirmingDeactivation(false)}
                disabled={submitting}
              >
                Keep active
              </button>
            </div>
          </div>
        ) : (
          <button
            type="button"
            className="button--quiet family-member-deactivate"
            onClick={() => setConfirmingDeactivation(true)}
            disabled={queued}
          >
            Deactivate profile
          </button>
        )
      ) : (
        <fetcher.Form
          method="post"
          action="/family"
          className="member-restore-form"
        >
          <input type="hidden" name="intent" value="restoreMember" />
          <input type="hidden" name="memberId" value={member.id} />
          <input type="hidden" name="memberName" value={member.displayName} />
          {memberAction?.intent === "restoreMember" && mutationError ? (
            <p className="error-message" role="alert">
              {mutationError}
            </p>
          ) : null}
          <button type="submit" disabled={submitting}>
            {submitting ? "Restoring…" : "Restore profile"}
          </button>
        </fetcher.Form>
      )}
    </li>
  );
}
