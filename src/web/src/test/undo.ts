import { screen } from "@testing-library/react";
import type { UserEvent } from "@testing-library/user-event";

/**
 * Presses the newest undo toast's dismiss button, which saves the queued
 * action straight away instead of waiting out the undo time.
 */
export async function saveQueuedAction(user: UserEvent): Promise<void> {
  const buttons = await screen.findAllByRole("button", {
    name: "Dismiss and save now",
  });
  await user.click(buttons.at(-1)!);
}
