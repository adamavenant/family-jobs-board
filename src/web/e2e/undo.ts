import { expect } from "@playwright/test";
import type { Page } from "@playwright/test";

/**
 * Presses the newest undo toast's dismiss control, which saves the queued
 * action straight away instead of waiting out the undo time.
 */
export async function saveQueuedAction(page: Page): Promise<void> {
  const dismiss = page
    .getByRole("button", { name: "Dismiss and save now" })
    .first();
  await expect(dismiss).toBeVisible();
  await dismiss.click();
}
