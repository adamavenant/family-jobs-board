import { useState } from "react";
import { Form, useActionData, useNavigation } from "react-router";

import type { AuthStart, PendingPinSetup, StartMember } from "../../api/auth";
import { ThemeToggle } from "../theme/ThemeToggle";

interface ActionFeedback {
  intent?: string;
  error?: string;
}

export function AuthPage({ state }: { state: AuthStart | PendingPinSetup }) {
  const feedback = useActionData() as ActionFeedback | undefined;
  const navigation = useNavigation();
  const submitting = navigation.state === "submitting";

  return (
    <main className="auth-page">
      <header className="auth-header">
        <p className="eyebrow">Family Jobs Board</p>
        <ThemeToggle />
      </header>
      <section className="auth-card" aria-labelledby="auth-heading">
        {state.state === "createFirstAdult" ? (
          <BootstrapForm
            mode="createFirstAdult"
            error={feedback?.error}
            submitting={submitting}
          />
        ) : state.state === "claimExistingAdult" ? (
          <BootstrapForm
            mode="claimExistingAdult"
            adults={state.adults}
            error={feedback?.error}
            submitting={submitting}
          />
        ) : state.state === "signIn" ? (
          <SignInForm
            members={state.members}
            error={feedback?.error}
            submitting={submitting}
          />
        ) : (
          <SetupPinForm
            setup={state}
            error={feedback?.error}
            submitting={submitting}
          />
        )}
      </section>
    </main>
  );
}

function BootstrapForm({
  mode,
  adults = [],
  error,
  submitting,
}: {
  mode: "createFirstAdult" | "claimExistingAdult";
  adults?: StartMember[];
  error: string | undefined;
  submitting: boolean;
}) {
  const fresh = mode === "createFirstAdult";
  return (
    <>
      <div className="auth-card__intro">
        <p className="eyebrow">First-time setup</p>
        <h1 id="auth-heading">
          {fresh ? "Create the first grown-up" : "Claim your grown-up profile"}
        </h1>
        <p>
          {fresh
            ? "Add one grown-up to protect the family board. You can set up everyone else afterwards."
            : "Your existing jobs and points are safe. Choose a grown-up and give that profile a private PIN."}
        </p>
      </div>
      <Form method="post" className="auth-form">
        <input type="hidden" name="intent" value="bootstrap" />
        <input type="hidden" name="mode" value={mode} />
        {fresh ? (
          <label>
            <span>First name</span>
            <input
              name="firstName"
              autoComplete="given-name"
              maxLength={100}
              required
              autoFocus
            />
          </label>
        ) : (
          <label>
            <span>Grown-up profile</span>
            <select name="memberId" required autoFocus>
              <option value="">Choose your profile</option>
              {adults.map((adult) => (
                <option key={adult.id} value={adult.id}>
                  {adult.displayName}
                </option>
              ))}
            </select>
          </label>
        )}
        <label>
          <span>Surname</span>
          <input
            name="surname"
            autoComplete="family-name"
            maxLength={100}
            required
          />
        </label>
        <PinFields length={6} />
        {error ? (
          <p className="error-message auth-form__message" role="alert">
            {error}
          </p>
        ) : null}
        <button type="submit" disabled={submitting}>
          {submitting ? "Setting up…" : "Set up and continue"}
        </button>
      </Form>
    </>
  );
}

function SignInForm({
  members,
  error,
  submitting,
}: {
  members: StartMember[];
  error: string | undefined;
  submitting: boolean;
}) {
  const [memberId, setMemberId] = useState(members[0]?.id ?? "");
  const selected = members.find((member) => member.id === memberId);
  const pinLength = selected?.role === "adult" ? 6 : 4;

  return (
    <>
      <div className="auth-card__intro">
        <p className="eyebrow">Welcome back</p>
        <h1 id="auth-heading">Who’s using the board?</h1>
        <p>Choose your profile, then enter your private PIN.</p>
      </div>
      {members.length === 0 ? (
        <p role="status">
          No profiles are ready to sign in yet. Ask a grown-up to finish setup.
        </p>
      ) : (
        <Form method="post" className="auth-form">
          <input type="hidden" name="intent" value="signIn" />
          <input type="hidden" name="role" value={selected?.role ?? "child"} />
          <label>
            <span>Profile</span>
            <select
              key={error ? "error" : "ready"}
              name="memberId"
              value={memberId}
              onChange={(event) => setMemberId(event.target.value)}
              autoFocus={Boolean(error)}
              required
            >
              {members.map((member) => (
                <option key={member.id} value={member.id}>
                  {member.displayName} —{" "}
                  {member.role === "adult" ? "Grown-up" : "Child"}
                </option>
              ))}
            </select>
          </label>
          <label>
            <span>{pinLength}-digit PIN</span>
            <input
              key={memberId}
              name="pin"
              type="password"
              inputMode="numeric"
              autoComplete="current-password"
              pattern={`[0-9]{${pinLength}}`}
              minLength={pinLength}
              maxLength={pinLength}
              required
            />
          </label>
          {error ? (
            <p className="error-message auth-form__message" role="alert">
              {error}
            </p>
          ) : null}
          <button type="submit" disabled={submitting}>
            {submitting ? "Checking…" : "Open my board"}
          </button>
        </Form>
      )}
    </>
  );
}

function SetupPinForm({
  setup,
  error,
  submitting,
}: {
  setup: PendingPinSetup;
  error: string | undefined;
  submitting: boolean;
}) {
  const pinLength = setup.targetRole === "adult" ? 6 : 4;
  return (
    <>
      <div className="auth-card__intro">
        <p className="eyebrow">Private handoff</p>
        <h1 id="auth-heading">Over to {setup.targetDisplayName}</h1>
        <p>
          The grown-up has been signed out. {setup.targetDisplayName} can now
          choose a private {pinLength}-digit PIN.
        </p>
      </div>
      <Form method="post" className="auth-form">
        <input type="hidden" name="intent" value="setupPin" />
        <input type="hidden" name="role" value={setup.targetRole} />
        <PinFields length={pinLength} autoFocus />
        {error ? (
          <p className="error-message auth-form__message" role="alert">
            {error}
          </p>
        ) : null}
        <button type="submit" disabled={submitting}>
          {submitting ? "Saving…" : "Save PIN and open board"}
        </button>
      </Form>
      <p className="auth-card__expiry" role="status">
        This handoff expires at{" "}
        {new Intl.DateTimeFormat("en", { timeStyle: "short" }).format(
          new Date(setup.expiresAtUtc),
        )}
        .
      </p>
    </>
  );
}

function PinFields({
  length,
  autoFocus = false,
}: {
  length: number;
  autoFocus?: boolean;
}) {
  return (
    <>
      <label>
        <span>{length}-digit PIN</span>
        <input
          name="pin"
          type="password"
          inputMode="numeric"
          autoComplete="new-password"
          pattern={`[0-9]{${length}}`}
          minLength={length}
          maxLength={length}
          required
          autoFocus={autoFocus}
        />
      </label>
      <label>
        <span>Confirm PIN</span>
        <input
          name="pinConfirmation"
          type="password"
          inputMode="numeric"
          autoComplete="new-password"
          pattern={`[0-9]{${length}}`}
          minLength={length}
          maxLength={length}
          required
        />
      </label>
    </>
  );
}
