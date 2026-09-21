import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type { GoodBehaviourType } from "../../api/goodBehaviours";
import type { HouseholdMember } from "../../api/today";
import { createRequestId } from "../../app/requestId";
import { useSuccessToast } from "../../app/SuccessToast";
import type { GoodBehavioursActionResult } from "./goodBehavioursRoute";

type BehaviourFetcher = ReturnType<
  typeof useFetcher<GoodBehavioursActionResult>
>;

export function GoodBehavioursPanel({
  children,
}: {
  children: HouseholdMember[];
}) {
  const fetcher = useFetcher<GoodBehavioursActionResult>();
  const { showSuccess } = useSuccessToast();
  const types = fetcher.data?.types ?? [];
  const message = fetcher.data?.error ? undefined : fetcher.data?.message;

  useEffect(() => {
    if (fetcher.state === "idle" && message) {
      showSuccess(message);
    }
  }, [fetcher.state, message, showSuccess]);

  return (
    <details
      className="grown-up-tools good-behaviours"
      onToggle={(event) => {
        if (
          event.currentTarget.open &&
          fetcher.state === "idle" &&
          fetcher.data === undefined
        ) {
          void fetcher.load("/good-behaviours");
        }
      }}
    >
      <summary>
        <span className="eyebrow">Good behaviours</span>
        <span className="grown-up-tools__action">
          Log or manage good behaviours <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <div className="family-members__intro">
          <div>
            <h2>Good behaviours</h2>
            <p>
              Log a kind, helpful or brave moment and the points are added
              straight away. You can change the points each time.
            </p>
          </div>
          <LogBehaviourForm
            fetcher={fetcher}
            childMembers={children}
            types={types}
          />
        </div>

        {fetcher.state === "loading" && fetcher.data === undefined ? (
          <p role="status">Loading good behaviours…</p>
        ) : fetcher.data?.error && fetcher.data.types === undefined ? (
          <div className="family-members__load-error">
            <p className="error-message" role="alert">
              {fetcher.data.error}
            </p>
            <button
              type="button"
              onClick={() => void fetcher.load("/good-behaviours")}
            >
              Try again
            </button>
          </div>
        ) : (
          <section aria-labelledby="behaviour-types-heading">
            <h3 id="behaviour-types-heading">Behaviour types</h3>
            {types.length === 0 ? (
              <p className="inactive-family-members__empty">
                No good behaviours yet. Add the first one below.
              </p>
            ) : (
              <ul className="good-behaviour-list" aria-label="Good behaviours">
                {types.map((type) => (
                  <TypeCard key={type.id} type={type} fetcher={fetcher} />
                ))}
              </ul>
            )}
            <CreateTypeForm fetcher={fetcher} />
          </section>
        )}
      </div>
    </details>
  );
}

function safeRequestId(): string | null {
  try {
    return createRequestId();
  } catch {
    return null;
  }
}

function LogBehaviourForm({
  fetcher,
  childMembers,
  types,
}: {
  fetcher: BehaviourFetcher;
  childMembers: HouseholdMember[];
  types: GoodBehaviourType[];
}) {
  const [typeId, setTypeId] = useState("");
  const [pointsOverride, setPointsOverride] = useState<string | null>(null);
  const [requestId, setRequestId] = useState(safeRequestId);
  const [handledResult, setHandledResult] = useState<unknown>(null);
  const selected = types.find((type) => type.id === typeId) ?? types[0];
  const result =
    fetcher.data?.intent === "logBehaviour" ? fetcher.data : undefined;
  const submitting = fetcher.state !== "idle";

  // A retry after an error reuses the request ID so the server can award once;
  // only a successful log starts a fresh request.
  if (
    fetcher.state === "idle" &&
    result &&
    !result.error &&
    result !== handledResult
  ) {
    setHandledResult(result);
    setRequestId(safeRequestId());
    setPointsOverride(null);
  }

  return (
    <fetcher.Form
      method="post"
      action="/good-behaviours"
      className="family-member-form"
      aria-label="Log a good behaviour"
    >
      <input type="hidden" name="intent" value="logBehaviour" />
      <input type="hidden" name="requestId" value={requestId ?? ""} />
      <div className="form-group">
        <label htmlFor="behaviour-child">Child</label>
        <select id="behaviour-child" name="childId" required>
          {childMembers.map((child) => (
            <option key={child.id} value={child.id}>
              {child.displayName}
            </option>
          ))}
        </select>
      </div>
      <div className="form-group">
        <label htmlFor="behaviour-type">Good behaviour</label>
        <select
          id="behaviour-type"
          name="typeId"
          required
          value={selected?.id ?? ""}
          onChange={(event) => {
            setTypeId(event.target.value);
            setPointsOverride(null);
          }}
        >
          {types.map((type) => (
            <option key={type.id} value={type.id}>
              {type.name}
            </option>
          ))}
        </select>
      </div>
      <div className="form-group">
        <label htmlFor="behaviour-points">Points to award</label>
        <input
          id="behaviour-points"
          name="points"
          type="number"
          min="0"
          step="1"
          required
          value={pointsOverride ?? String(selected?.points ?? 0)}
          onChange={(event) => setPointsOverride(event.target.value)}
        />
      </div>
      {result?.error ? (
        <p className="error-message" role="alert">
          {result.error}
        </p>
      ) : null}
      <button type="submit" disabled={submitting || selected === undefined}>
        {submitting && fetcher.formData?.get("intent") === "logBehaviour"
          ? "Logging…"
          : "Log behaviour"}
      </button>
    </fetcher.Form>
  );
}

function CreateTypeForm({ fetcher }: { fetcher: BehaviourFetcher }) {
  const formRef = useRef<HTMLFormElement>(null);
  const result =
    fetcher.data?.intent === "createType" ? fetcher.data : undefined;
  const created = result && !result.error ? result.affectedTypeId : undefined;

  useEffect(() => {
    if (fetcher.state === "idle" && created) {
      formRef.current?.reset();
    }
  }, [created, fetcher.state]);

  return (
    <fetcher.Form
      method="post"
      action="/good-behaviours"
      className="family-member-form"
      aria-label="Add a good behaviour"
      ref={formRef}
    >
      <input type="hidden" name="intent" value="createType" />
      <TypeFields idPrefix="new-behaviour" />
      {result?.error ? (
        <p className="error-message" role="alert">
          {result.error}
        </p>
      ) : null}
      <button type="submit" disabled={fetcher.state !== "idle"}>
        Add good behaviour
      </button>
    </fetcher.Form>
  );
}

function TypeFields({
  idPrefix,
  type,
}: {
  idPrefix: string;
  type?: GoodBehaviourType;
}) {
  return (
    <>
      <div className="form-group">
        <label htmlFor={`${idPrefix}-name`}>Name</label>
        <input
          id={`${idPrefix}-name`}
          name="name"
          maxLength={100}
          defaultValue={type?.name}
          required
        />
      </div>
      <div className="form-group">
        <label htmlFor={`${idPrefix}-points`}>Usual points</label>
        <input
          id={`${idPrefix}-points`}
          name="points"
          type="number"
          min="0"
          step="1"
          defaultValue={type?.points ?? 5}
          required
        />
      </div>
      <div className="form-group good-behaviour-description">
        <label htmlFor={`${idPrefix}-description`}>
          Description (optional)
        </label>
        <input
          id={`${idPrefix}-description`}
          name="description"
          maxLength={500}
          defaultValue={type?.description}
        />
      </div>
    </>
  );
}

function TypeCard({
  type,
  fetcher,
}: {
  type: GoodBehaviourType;
  fetcher: BehaviourFetcher;
}) {
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const editRef = useRef<HTMLDetailsElement>(null);
  const action =
    fetcher.data?.affectedTypeId === type.id ? fetcher.data : undefined;
  const error =
    action?.intent === "updateType" || action?.intent === "deleteType"
      ? action.error
      : undefined;
  const submitting = fetcher.state !== "idle";

  useEffect(() => {
    if (
      fetcher.state === "idle" &&
      action?.intent === "updateType" &&
      !action.error &&
      editRef.current
    ) {
      editRef.current.open = false;
    }
  }, [action, fetcher.state]);

  return (
    <li>
      <div className="family-member-list__identity">
        <strong>{type.name}</strong>
        {type.description ? <span>{type.description}</span> : null}
      </div>
      <span className="good-behaviour-list__points">
        {type.points} {type.points === 1 ? "point" : "points"}
      </span>
      <details className="family-member-edit" ref={editRef}>
        <summary>Edit {type.name}</summary>
        <fetcher.Form method="post" action="/good-behaviours">
          <input type="hidden" name="intent" value="updateType" />
          <input type="hidden" name="typeId" value={type.id} />
          <TypeFields idPrefix={`edit-${type.id}`} type={type} />
          {action?.intent === "updateType" && error ? (
            <p className="error-message" role="alert">
              {error}
            </p>
          ) : null}
          <button type="submit" disabled={submitting}>
            Save {type.name}
          </button>
        </fetcher.Form>
      </details>
      {confirmingDelete ? (
        <div
          className="deactivate-confirmation"
          role="group"
          aria-label={`Delete ${type.name}`}
        >
          <p>
            Delete {type.name}? It can no longer be logged. Points already
            awarded stay exactly as they are.
          </p>
          {action?.intent === "deleteType" && error ? (
            <p className="error-message" role="alert">
              {error}
            </p>
          ) : null}
          <div>
            <fetcher.Form method="post" action="/good-behaviours">
              <input type="hidden" name="intent" value="deleteType" />
              <input type="hidden" name="typeId" value={type.id} />
              <button
                type="submit"
                className="button--danger"
                disabled={submitting}
              >
                Yes, delete
              </button>
            </fetcher.Form>
            <button
              type="button"
              className="button--quiet"
              onClick={() => setConfirmingDelete(false)}
              disabled={submitting}
            >
              Keep it
            </button>
          </div>
        </div>
      ) : (
        <button
          type="button"
          className="button--quiet"
          onClick={() => setConfirmingDelete(true)}
        >
          Delete {type.name}
        </button>
      )}
    </li>
  );
}
