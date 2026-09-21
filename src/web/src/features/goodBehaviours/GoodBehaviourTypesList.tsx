import { useFetcher } from "react-router";

import type { GoodBehavioursActionResult } from "./goodBehavioursRoute";

export function GoodBehaviourTypesList() {
  const fetcher = useFetcher<GoodBehavioursActionResult>();
  const types = fetcher.data?.types ?? [];

  return (
    <details
      className="grown-up-tools good-behaviours good-behaviours--child"
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
          Ways to earn points <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <p className="good-behaviours__note">
          A grown-up can log these when they see them. The points are usually
          the amount shown, but a grown-up may change them.
        </p>
        {fetcher.state === "loading" && fetcher.data === undefined ? (
          <p role="status">Loading good behaviours…</p>
        ) : fetcher.data?.error ? (
          <p className="error-message" role="alert">
            {fetcher.data.error}
          </p>
        ) : types.length === 0 ? (
          <p className="inactive-family-members__empty">
            No good behaviours have been set up yet.
          </p>
        ) : (
          <ul className="good-behaviour-list" aria-label="Good behaviours">
            {types.map((type) => (
              <li key={type.id}>
                <div className="family-member-list__identity">
                  <strong>{type.name}</strong>
                  {type.description ? <span>{type.description}</span> : null}
                </div>
                <span className="good-behaviour-list__points">
                  usually {type.points} {type.points === 1 ? "point" : "points"}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </details>
  );
}
