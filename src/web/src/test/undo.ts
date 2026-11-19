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
 * Starts recording every value `read` returns as the page changes, and returns a
 * function that stops and lists them. A wrong state can last a single render, so only
 * recording every change shows that it never appeared.
 */
export function recordChanges(read: () => readonly string[]): () => string[] {
  const seen = new Set<string>();
  const record = () => read().forEach((value) => seen.add(value));
  const observer = new MutationObserver(record);
  observer.observe(document.body, {
    attributes: true,
    characterData: true,
    childList: true,
    subtree: true,
  });
  record();
  return () => {
    observer.disconnect();
    return [...seen];
  };
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
