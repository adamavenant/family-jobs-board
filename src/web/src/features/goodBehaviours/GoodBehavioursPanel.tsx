import { useEffect, useRef, useState } from "react";
import { useFetcher } from "react-router";

import type { GoodBehaviourType } from "../../api/goodBehaviours";
import type { HouseholdMember } from "../../api/today";
import { formText } from "../../app/actionDescriptions";
import { createRequestId } from "../../app/requestId";
import { useUndoQueue } from "../../app/undoQueue";
import type { QueuedAction } from "../../app/undoQueue";
import { ChildAssignmentPicker } from "../today/ChildAssignmentPicker";
import type {
  GoodBehavioursActionResult,
  GoodBehavioursLoaderData,
} from "./goodBehavioursRoute";

export function GoodBehavioursPanel({
  children,
}: {
  children: HouseholdMember[];
}) {
  const fetcher = useFetcher<GoodBehavioursLoaderData>();
  const { actions } = useUndoQueue();
  const loadedTypes = fetcher.data?.types ?? [];
  const types = showQueuedTypeChanges(loadedTypes, actions);
  const hasLoadedTypes = fetcher.data?.types !== undefined;
  const adding = actions.filter(
    (action) => action.formData.get("intent") === "createType",
  );

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
          {hasLoadedTypes ? (
            <LogBehaviourForm
              childMembers={children}
              types={types.map(({ type }) => type)}
            />
          ) : null}
        </div>

        {!hasLoadedTypes && fetcher.data?.error ? (
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
        ) : !hasLoadedTypes ? (
          <p role="status">Loading good behaviours…</p>
        ) : (
          <section aria-labelledby="behaviour-types-heading">
            <h3 id="behaviour-types-heading">Behaviour types</h3>
            {types.length === 0 && adding.length === 0 ? (
              <p className="inactive-family-members__empty">
                No good behaviours yet. Add the first one below.
              </p>
            ) : (
              <ul className="good-behaviour-list" aria-label="Good behaviours">
                {types.map(({ type, queued }) => (
                  <TypeCard key={type.id} type={type} queued={queued} />
                ))}
                {adding.map((action) => (
                  <li key={action.id} className="good-behaviour--queued">
                    <div className="family-member-list__identity">
                      <strong>
                        {formText(action.formData, "name").trim()}
                      </strong>
                      <span>Being added</span>
                    </div>
                    <span className="good-behaviour-list__points">
                      {formText(action.formData, "points")} points
                    </span>
                  </li>
                ))}
              </ul>
            )}
            <CreateTypeForm />
          </section>
        )}
      </div>
    </details>
  );
}

/** Types as they will be once queued edits and deletions are saved. */
function showQueuedTypeChanges(
  types: GoodBehaviourType[],
  actions: readonly QueuedAction[],
): { type: GoodBehaviourType; queued: boolean }[] {
  return types.flatMap(
    (type): { type: GoodBehaviourType; queued: boolean }[] => {
      const form = actions.find(
        (action) =>
          action.formData.get("typeId") === type.id &&
          (action.formData.get("intent") === "updateType" ||
            action.formData.get("intent") === "deleteType"),
      )?.formData;
      if (!form) {
        return [{ type, queued: false }];
      }
      if (form.get("intent") === "deleteType") {
        return [];
      }

      const points = Number(formText(form, "points"));
      return [
        {
          type: {
            ...type,
            name: formText(form, "name").trim() || type.name,
            description: formText(form, "description").trim(),
            points: Number.isInteger(points) ? points : type.points,
          },
          queued: true,
        },
      ];
    },
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
  childMembers,
  types,
}: {
  childMembers: HouseholdMember[];
  types: GoodBehaviourType[];
}) {
  const fetcher = useFetcher<GoodBehavioursActionResult>();
  const [typeId, setTypeId] = useState("");
  const [pointsOverride, setPointsOverride] = useState<string | null>(null);
  const [requestId, setRequestId] = useState(safeRequestId);
  const [handledResult, setHandledResult] = useState<unknown>(null);
  const selected = types.find((type) => type.id === typeId) ?? types[0];
  const result =
    fetcher.data?.intent === "logBehaviour" ? fetcher.data : undefined;
  const submitting = fetcher.state !== "idle";

  // A retry after an error reuses the request ID so the server can award once;
  // only a queued log starts a fresh request.
  if (fetcher.state === "idle" && result?.queued && result !== handledResult) {
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
      <input type="hidden" name="typeName" value={selected?.name ?? ""} />
      <input type="hidden" name="typePoints" value={selected?.points ?? 0} />
      <ChildAssignmentPicker children={childMembers} legend="Log for" />
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

function CreateTypeForm() {
  const fetcher = useFetcher<GoodBehavioursActionResult>();
  const formRef = useRef<HTMLFormElement>(null);
  const result =
    fetcher.data?.intent === "createType" ? fetcher.data : undefined;

  useEffect(() => {
    if (fetcher.state === "idle" && result?.queued) {
      formRef.current?.reset();
    }
  }, [fetcher.state, result]);

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
  queued,
}: {
  type: GoodBehaviourType;
  queued: boolean;
}) {
  const fetcher = useFetcher<GoodBehavioursActionResult>();
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const editRef = useRef<HTMLDetailsElement>(null);
  const action = fetcher.data;
  const error = action?.error;
  // A queued edit can still be undone; until it is saved the type is locked.
  const submitting = fetcher.state !== "idle" || queued;

  useEffect(() => {
    if (
      fetcher.state === "idle" &&
      action?.intent === "updateType" &&
      action.queued &&
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
              <input type="hidden" name="typeName" value={type.name} />
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
          disabled={queued}
        >
          Delete {type.name}
        </button>
      )}
    </li>
  );
}
