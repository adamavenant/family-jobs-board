import type { ChangeEvent } from "react";

import type { HouseholdMember } from "../../api/today";

export function ChildAssignmentPicker({
  children,
  legend,
  selectedChildIds,
  onSelectionChange,
}: {
  children: HouseholdMember[];
  legend: string;
  selectedChildIds?: string[];
  onSelectionChange?: (childIds: string[]) => void;
}) {
  return (
    <fieldset className="assignee-picker" aria-required="true">
      <legend>{legend}</legend>
      <div className="assignee-picker__options">
        {children.map((child, index) => (
          <label key={child.id} className="assignee-option">
            <input
              type="checkbox"
              name="childIds"
              value={child.id}
              {...(selectedChildIds === undefined
                ? { defaultChecked: index === 0 }
                : {
                    checked: selectedChildIds.includes(child.id),
                    onChange: (event: ChangeEvent<HTMLInputElement>) => {
                      const nextIds = event.target.checked
                        ? children
                            .filter(
                              (candidate) =>
                                candidate.id === child.id ||
                                selectedChildIds.includes(candidate.id),
                            )
                            .map((candidate) => candidate.id)
                        : selectedChildIds.filter((id) => id !== child.id);
                      onSelectionChange?.(nextIds);
                    },
                  })}
            />
            <span>{child.displayName}</span>
          </label>
        ))}
      </div>
      <small>Choose the children this applies to.</small>
    </fieldset>
  );
}
