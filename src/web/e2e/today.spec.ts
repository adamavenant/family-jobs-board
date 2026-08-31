import { expect, test } from "@playwright/test";

test("adds, completes, and approves a job exactly once", async ({ page }) => {
  const jobName = `Playwright job ${Date.now()}`;

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: /Good day, Alex!/ }),
  ).toBeVisible();
  const initialBalance = Number(
    await page.locator(".hero__balance strong").innerText(),
  );

  await page.getByLabel("Job name").fill(jobName);
  await page
    .getByLabel("Description")
    .fill("Created through the public browser interface.");
  await page.getByRole("spinbutton", { name: "Points", exact: true }).fill("3");
  await page.getByRole("button", { name: "Add job" }).click();

  const heading = page.getByRole("heading", { name: jobName, exact: true });
  await expect(heading).toBeVisible();
  await expect(page.getByText("Job added to today’s board.")).toBeVisible();
  await expect(page.getByLabel("Job name")).toHaveValue("");

  const card = page.getByRole("article").filter({ has: heading });
  await card.getByRole("button", { name: "Mark as done" }).click();

  await card.getByRole("button", { name: "Approve +3 points" }).click();

  await expect(card.getByText("Approved — 3 points awarded")).toBeVisible();
  await expect(
    page.getByLabel(`${initialBalance + 3} points earned`),
  ).toBeVisible();
});

test("shows a server failure and preserves the entered job", async ({
  page,
}) => {
  await page.route("**/api/today/jobs", async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 250));
    await route.fulfill({
      status: 503,
      contentType: "application/problem+json",
      body: JSON.stringify({ detail: "The database is unavailable." }),
    });
  });

  await page.goto("/");
  await page.getByLabel("Job name").fill("Keep this job");
  await page.getByRole("spinbutton", { name: "Points", exact: true }).fill("4");

  const addButton = page.getByRole("button", { name: "Add job" });
  await addButton.click();
  await expect(page.getByRole("button", { name: "Adding…" })).toBeDisabled();
  await expect(page.getByRole("alert")).toHaveText(
    "The database is unavailable.",
  );
  await expect(page.getByLabel("Job name")).toHaveValue("Keep this job");
});

test("persists the selected dark theme after reload", async ({ page }) => {
  await page.goto("/");

  await page.getByRole("button", { name: "Dark mode" }).click();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");

  await page.reload();

  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await expect(
    page.getByRole("button", { name: "Light mode" }),
  ).toHaveAttribute("aria-pressed", "true");
});
