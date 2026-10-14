import { useEffect } from "react";
import { useFetcher } from "react-router";

import {
  maximumUndoGracePeriodSeconds,
  minimumUndoGracePeriodSeconds,
  undoQueue,
} from "../../app/undoQueue";
import type {
  HouseholdSettingsActionResult,
  HouseholdSettingsLoaderData,
} from "./householdSettingsRoute";

export function HouseholdSettingsPanel() {
  const settings = useFetcher<HouseholdSettingsLoaderData>();
  const save = useFetcher<HouseholdSettingsActionResult>();
  const current = settings.data?.settings;
  const saved = save.data?.settings;
  const saving = save.state !== "idle";

  useEffect(() => {
    if (save.state === "idle" && saved) {
      undoQueue.notify(
        `Undo time saved: ${saved.undoGracePeriodSeconds} seconds on every device.`,
      );
    }
  }, [save.state, saved]);

  return (
    <details
      className="grown-up-tools household-settings"
      onToggle={(event) => {
        // Reload on every open: another grown-up may have changed it elsewhere.
        if (event.currentTarget.open && settings.state === "idle") {
          void settings.load("/household-settings");
        }
      }}
    >
      <summary>
        <span className="eyebrow">Settings</span>
        <span className="grown-up-tools__action">
          Undo time <span aria-hidden="true">+</span>
        </span>
      </summary>
      <div className="family-members__content">
        <div className="family-members__intro">
          <div>
            <h2>Undo time</h2>
            <p>
              After someone marks a job done, approves it, or makes another
              change, a message offers Undo for this long before the change is
              saved. It applies to every device in the household.
            </p>
          </div>
          {current ? (
            <save.Form
              // Start from the latest saved value whenever it changes.
              key={current.undoGracePeriodSeconds}
              method="post"
              action="/household-settings"
              className="family-member-form"
              aria-label="Undo time"
            >
              <div className="form-group">
                <label htmlFor="undo-grace-period">Seconds to undo</label>
                <input
                  id="undo-grace-period"
                  name="undoGracePeriodSeconds"
                  type="number"
                  inputMode="numeric"
                  min={minimumUndoGracePeriodSeconds}
                  max={maximumUndoGracePeriodSeconds}
                  step={1}
                  defaultValue={current.undoGracePeriodSeconds}
                  aria-describedby="undo-grace-period-hint"
                  required
                />
                <small id="undo-grace-period-hint">
                  Whole seconds from {minimumUndoGracePeriodSeconds} to{" "}
                  {maximumUndoGracePeriodSeconds}. New households start at 30.
                </small>
              </div>
              {save.data?.error ? (
                <p className="error-message" role="alert">
                  {save.data.error}
                </p>
              ) : null}
              <button type="submit" disabled={saving}>
                {saving ? "Saving…" : "Save undo time"}
              </button>
            </save.Form>
          ) : settings.data?.error ? (
            <div className="family-members__load-error">
              <p className="error-message" role="alert">
                {settings.data.error}
              </p>
              <button
                type="button"
                onClick={() => void settings.load("/household-settings")}
              >
                Try again
              </button>
            </div>
          ) : (
            <p role="status">Loading…</p>
          )}
        </div>
      </div>
    </details>
  );
}
