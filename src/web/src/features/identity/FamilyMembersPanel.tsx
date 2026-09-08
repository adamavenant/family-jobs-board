import { useFetcher } from "react-router";

import type {
  AppActionResult,
  FamilyMembersActionResult,
} from "../../app/routes";
import type { FamilyMember } from "../../api/members";

export function FamilyMembersPanel() {
  const family = useFetcher<FamilyMembersActionResult>();
  const members = family.data?.members ?? [];
  const created = members.find(
    (member) => member.id === family.data?.createdMemberId,
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
            {created ? (
              <p className="success-message" role="status">
                {created.displayName} was added. Set up their PIN now or come
                back later.
              </p>
            ) : null}
            <ul className="family-member-list" aria-label="Family members">
              {members.map((member) => (
                <FamilyMemberCard key={member.id} member={member} />
              ))}
            </ul>
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

function FamilyMemberCard({ member }: { member: FamilyMember }) {
  const handoff = useFetcher<AppActionResult>();
  const error =
    handoff.data?.intent === "beginPinSetup" ? handoff.data.error : undefined;
  const fullName = [member.firstName, member.surname].filter(Boolean).join(" ");
  return (
    <li>
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
      </div>
      {!member.isCredentialReady ? (
        <handoff.Form method="post" action="/" className="member-handoff-form">
          <input type="hidden" name="intent" value="beginPinSetup" />
          <input type="hidden" name="memberId" value={member.id} />
          {member.surname === null ? (
            <label>
              <span>Surname</span>
              <input name="surname" maxLength={100} required />
            </label>
          ) : null}
          {error ? (
            <p className="error-message" role="alert">
              {error}
            </p>
          ) : null}
          <button type="submit" disabled={handoff.state !== "idle"}>
            {handoff.state !== "idle" ? "Starting…" : "Set up PIN now"}
          </button>
        </handoff.Form>
      ) : null}
    </li>
  );
}
