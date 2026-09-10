import { useState } from "react";
import { useFetcher } from "react-router";

import type {
  AppActionResult,
  FamilyMembersActionResult,
} from "../../app/routes";
import type { FamilyMember } from "../../api/members";

export function FamilyMembersPanel({
  currentMemberId,
}: {
  currentMemberId: string;
}) {
  const family = useFetcher<FamilyMembersActionResult>();
  const members = family.data?.members ?? [];
  const affected = members.find(
    (member) => member.id === family.data?.affectedMemberId,
  );
  const active = members.filter((member) => member.isActive);
  const inactive = members.filter((member) => !member.isActive);
  const success = actionSuccess(
    family.data?.intent,
    family.data?.error ? undefined : affected,
  );

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
          <CreateMemberForm fetcher={family} />
        </div>

        {family.state === "loading" && family.data === undefined ? (
          <p role="status">Loading family members…</p>
        ) : family.data?.error && family.data.members === undefined ? (
          <div className="family-members__load-error">
            <p className="error-message" role="alert">
              {family.data.error}
            </p>
            <button type="button" onClick={() => void family.load("/family")}>
              Try again
            </button>
          </div>
        ) : (
          <>
            {success ? (
              <p className="success-message" role="status">
                {success}
              </p>
            ) : null}
            <ul className="family-member-list" aria-label="Family members">
              {active.map((member) => (
                <FamilyMemberCard
                  key={`${member.id}-${member.isActive}`}
                  member={member}
                  currentMemberId={currentMemberId}
                  fetcher={family}
                />
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
                  {inactive.map((member) => (
                    <FamilyMemberCard
                      key={`${member.id}-${member.isActive}`}
                      member={member}
                      currentMemberId={currentMemberId}
                      fetcher={family}
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

function CreateMemberForm({
  fetcher,
}: {
  fetcher: ReturnType<typeof useFetcher<FamilyMembersActionResult>>;
}) {
  const error =
    fetcher.data?.intent === "createMember" ? fetcher.data.error : undefined;
  const submitting = fetcher.state === "submitting";
  return (
    <fetcher.Form method="post" action="/family" className="family-member-form">
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
  fetcher,
}: {
  member: FamilyMember;
  currentMemberId: string;
  fetcher: ReturnType<typeof useFetcher<FamilyMembersActionResult>>;
}) {
  const handoff = useFetcher<AppActionResult>();
  const [confirmingDeactivation, setConfirmingDeactivation] = useState(false);
  const handoffError =
    handoff.data?.intent === "beginPinSetup" ? handoff.data.error : undefined;
  const memberAction =
    fetcher.data?.affectedMemberId === member.id ? fetcher.data : undefined;
  const mutationError =
    memberAction?.intent !== "createMember" ? memberAction?.error : undefined;
  const submitting = fetcher.state !== "idle";
  const fullName = [member.firstName, member.surname].filter(Boolean).join(" ");
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
      <details className="family-member-edit">
        <summary>Edit profile</summary>
        <fetcher.Form method="post" action="/family">
          <input type="hidden" name="intent" value="updateMember" />
          <input type="hidden" name="memberId" value={member.id} />
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

function actionSuccess(
  intent: FamilyMembersActionResult["intent"],
  member: FamilyMember | undefined,
): string | undefined {
  if (!member) {
    return undefined;
  }

  switch (intent) {
    case "createMember":
      return `${member.displayName} was added. Set up their PIN now or come back later.`;
    case "updateMember":
      return `${member.displayName}'s profile was updated.`;
    case "deactivateMember":
      return `${member.displayName}'s profile is inactive. Their history is still here.`;
    case "restoreMember":
      return `${member.displayName}'s profile was restored.`;
    default:
      return undefined;
  }
}
