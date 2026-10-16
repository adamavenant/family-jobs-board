// Helpers for the short descriptions undo toasts show. Forms send display
// names as hidden fields (for example "childName.<id>") so route actions can
// describe what is about to be saved without another request.

export function formText(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === "string" ? value : "";
}

export function quoted(value: string): string {
  return `“${value.trim()}”`;
}

export function joinNames(names: string[]): string {
  if (names.length <= 1) {
    return names[0] ?? "";
  }

  return `${names.slice(0, -1).join(", ")} and ${names.at(-1)}`;
}

export function childName(form: FormData, childId: string): string {
  return formText(form, `childName.${childId}`) || "the child";
}

/** "Sam and Alex" for the children ticked in a `childIds` picker. */
export function selectedChildNames(form: FormData): string {
  return joinNames(
    form
      .getAll("childIds")
      .filter((value): value is string => typeof value === "string")
      .map((id) => childName(form, id)),
  );
}

export function pointsText(points: number): string {
  return points === 1 ? "1 point" : `${points} points`;
}
