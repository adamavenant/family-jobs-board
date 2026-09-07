import type { HouseholdMember } from "../../api/today";

export function ChildAssignmentPicker({
  children,
  legend,
}: {
  children: HouseholdMember[];
  legend: string;
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
              defaultChecked={index === 0}
            />
            <span>{child.displayName}</span>
          </label>
        ))}
      </div>
      <small>Choose one child or select both.</small>
    </fieldset>
  );
}
