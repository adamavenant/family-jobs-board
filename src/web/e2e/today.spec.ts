import { expect, test } from "@playwright/test";

test("creates a daily recurring job and completes today's occurrence", async ({
  page,
}) => {
  const jobName = `Daily Playwright job ${Date.now()}`;
  const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";

  await page.goto(`/?member=${addieId}`);
  const recurringTools = page.locator("details.grown-up-tools--recurring");
  await recurringTools.locator("summary").click();
  await page
    .getByLabel("Assign daily job to")
    .selectOption({ label: "Fredster" });
  await page.getByLabel("Daily job name").fill(jobName);
  await page
    .getByLabel("Daily job description")
    .fill("Created as a daily routine through the browser.");
  await page.getByLabel("Daily job points").fill("2");
  await page.getByLabel("Daily job part of day").selectOption("morning");
  await page.getByLabel("Daily job time (optional)").fill("07:30");
  await page.getByRole("button", { name: "Create daily job" }).click();

  const heading = page.getByRole("heading", { name: jobName, exact: true });
  await expect(heading).toBeVisible();
  await expect(page.getByText(/Daily job created through/)).toBeVisible();
  const card = page.getByRole("article").filter({ has: heading });
  await expect(card.getByText("Daily · Morning · 07:30")).toBeVisible();

  await page
    .getByLabel("Viewing as")
    .selectOption({ label: "Fredster — Child" });
  await card.getByRole("button", { name: "Mark as done" }).click();
  await expect(card.getByText("Sent to a grown-up for approval")).toBeVisible();
});

test("creates a weekly recurring job for selected weekdays", async ({
  page,
}) => {
  const jobName = `Weekly Playwright job ${Date.now()}`;
  const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";

  await page.goto(`/?member=${addieId}`);
  const recurringTools = page.locator("details.grown-up-tools--recurring");
  await recurringTools.locator("summary").click();
  await page.getByLabel("Repeats").selectOption("weekly");
  for (const weekday of [
    "Monday",
    "Tuesday",
    "Wednesday",
    "Thursday",
    "Friday",
    "Saturday",
    "Sunday",
  ]) {
    await page.getByRole("checkbox", { name: weekday }).check();
  }
  await page
    .getByLabel("Assign weekly job to")
    .selectOption({ label: "Harrie" });
  await page.getByLabel("Weekly job name").fill(jobName);
  await page
    .getByLabel("Weekly job description")
    .fill("Created as a weekly routine through the browser.");
  await page.getByLabel("Weekly job points").fill("3");
  await page.getByLabel("Weekly job part of day").selectOption("evening");
  await page.getByLabel("Weekly job time (optional)").fill("18:15");
  await page.getByRole("button", { name: "Create weekly job" }).click();

  const heading = page.getByRole("heading", { name: jobName, exact: true });
  await expect(heading).toBeVisible();
  await expect(page.getByText(/Weekly job created through/)).toBeVisible();
  const card = page.getByRole("article").filter({ has: heading });
  await expect(card.getByText("Weekly · Evening · 18:15")).toBeVisible();
});

test("rejects a completed job, retries it, and then awards points", async ({
  page,
}) => {
  const jobName = `Playwright job ${Date.now()}`;
  const addieId = "22eb0cc1-058e-4b2e-bb18-d7aaad564a6c";

  await page.goto(`/?member=${addieId}`);
  await expect(
    page.getByRole("heading", { name: /Good day, Addie!/ }),
  ).toBeVisible();
  const grownUpTools = page.locator("details.grown-up-tools--one-off");
  await expect(grownUpTools).not.toHaveAttribute("open", "");
  await grownUpTools.locator("summary").click();
  await expect(grownUpTools).toHaveAttribute("open", "");
  const points = page.getByRole("spinbutton", {
    name: "Points",
    exact: true,
  });
  await expect(points).toHaveValue("1");
  await page
    .getByLabel("Assign to", { exact: true })
    .selectOption({ label: "Fredster" });

  await page.getByLabel("Job name", { exact: true }).fill(jobName);
  await page
    .getByLabel("Description", { exact: true })
    .fill("Created through the public browser interface.");
  await points.fill("3");
  await page.getByRole("button", { name: "Add job" }).click();

  const heading = page.getByRole("heading", { name: jobName, exact: true });
  await expect(heading).toBeVisible();
  await expect(page.getByText("Job added to today’s board.")).toBeVisible();
  await expect(page.getByLabel("Job name", { exact: true })).toHaveValue("");
  await expect(points).toHaveValue("1");

  const card = page.getByRole("article").filter({ has: heading });
  await expect(card.locator(".job-card__assignee")).toHaveText("For Fredster");
  await page
    .getByLabel("Viewing as")
    .selectOption({ label: "Fredster — Child" });
  await expect(
    page.getByRole("heading", { name: /Good day, Fredster!/ }),
  ).toBeVisible();
  const initialBalance = Number(
    await page.locator(".hero__balance strong").innerText(),
  );
  await card.getByRole("button", { name: "Mark as done" }).click();

  await page.getByLabel("Viewing as").selectOption({ label: "Addie — Adult" });
  await card
    .getByRole("textbox", { name: "Rejection reason (optional)" })
    .fill("Please clean underneath it too.");
  await card.getByRole("button", { name: "Reject job" }).click();

  await page
    .getByLabel("Viewing as")
    .selectOption({ label: "Fredster — Child" });
  await expect(card.getByText("Needs another go")).toBeVisible();
  await expect(card.getByText("Please clean underneath it too.")).toBeVisible();
  await card.getByRole("button", { name: "Mark as done" }).click();
  await expect(card.getByText("Needs another go")).not.toBeVisible();

  await page.getByLabel("Viewing as").selectOption({ label: "Addie — Adult" });
  await card.getByRole("button", { name: "Approve +3 points" }).click();

  await expect(card.getByText("Approved — 3 points awarded")).toBeVisible();
  await page
    .getByLabel("Viewing as")
    .selectOption({ label: "Fredster — Child" });
  await expect(
    page.getByLabel(`${initialBalance + 3} points earned`),
  ).toBeVisible();
  const history = page.locator("section.points-history");
  const earning = history.getByRole("listitem").filter({ hasText: jobName });
  await expect(earning.getByText(jobName, { exact: true })).toBeVisible();
  await expect(earning.getByText("+3", { exact: true })).toBeVisible();
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

  await page.goto("/?member=22eb0cc1-058e-4b2e-bb18-d7aaad564a6c");
  await page.locator("details.grown-up-tools--one-off summary").click();
  await page.getByLabel("Job name", { exact: true }).fill("Keep this job");
  await page.getByRole("spinbutton", { name: "Points", exact: true }).fill("4");

  const addButton = page.getByRole("button", { name: "Add job" });
  await addButton.click();
  await expect(page.getByRole("button", { name: "Adding…" })).toBeDisabled();
  await expect(page.getByRole("alert")).toHaveText(
    "The database is unavailable.",
  );
  await expect(page.getByLabel("Job name", { exact: true })).toHaveValue(
    "Keep this job",
  );
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

test("persists the selected family member in the URL and local storage", async ({
  page,
}) => {
  await page.goto("/");
  await page.getByLabel("Viewing as").selectOption({ label: "Harrie — Child" });

  await expect(page).toHaveURL(/member=e22facf5-69ce-45ce-9dad-306eef1852c9/);
  await expect(
    page.getByRole("heading", { name: /Good day, Harrie!/ }),
  ).toBeVisible();

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: /Good day, Harrie!/ }),
  ).toBeVisible();
});
