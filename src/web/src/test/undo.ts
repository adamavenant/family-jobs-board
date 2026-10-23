import { screen } from "@testing-library/react";
import { expect } from "vitest";
import type { UserEvent } from "@testing-library/user-event";

/**
 * Presses the newest undo toast's dismiss button (newest comes first), which saves the queued
 * action straight away instead of waiting out the undo time.
 */
export async function saveQueuedAction(user: UserEvent): Promise<void> {
  const buttons = await screen.findAllByRole("button", {
    name: "Dismiss and save now",
  });
  await user.click(buttons[0]!);
}

/**
 * The page's shared status region announced this text. Toasts are announced
 * there, rather than by live regions that appear together with their text.
 */
export function expectAnnounced(text: string): void {
  const announcements = screen
    .getAllByRole("status")
    .map((region) => region.textContent ?? "");
  expect(
    announcements.some((announcement) => announcement.includes(text)),
  ).toBe(true);
}
