import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  createMemoryRouter,
  RouterProvider,
  type ActionFunctionArgs,
} from "react-router";
import { expect, it } from "vitest";

import type { HouseholdMember } from "../../api/today";
import { RecurringJobForm } from "./RecurringJobForm";

const children: HouseholdMember[] = [
  {
    id: "22222222-2222-4222-8222-222222222222",
    firstName: "Fred",
    nickname: "Fredster",
    displayName: "Fredster",
    isAdult: false,
  },
];

it("clears a chosen end date before creating the job", async () => {
  const submissions: FormData[] = [];
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <RecurringJobForm children={children} today="2026-10-15" />,
        action: async ({ request }: ActionFunctionArgs) => {
          submissions.push(await request.formData());
          return { intent: "addRecurring", queued: true };
        },
      },
    ],
    { initialEntries: ["/"] },
  );
  const user = userEvent.setup();
  render(<RouterProvider router={router} />);

  await user.click(screen.getByText("Routines"));
  await user.type(screen.getByLabelText("Daily job name"), "Feed the fish");
  const endDate = screen.getByLabelText("Daily job ends (optional)");
  expect(
    screen.queryByRole("button", { name: "Clear end date" }),
  ).not.toBeInTheDocument();

  await user.type(endDate, "2026-11-30");
  expect(endDate).toHaveValue("2026-11-30");
  await user.click(screen.getByRole("button", { name: "Clear end date" }));

  expect(endDate).toHaveValue("");
  expect(endDate).toHaveFocus();
  expect(
    screen.queryByRole("button", { name: "Clear end date" }),
  ).not.toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Create daily job" }));

  // Queued behind its undo toast, the job resets the form straight away.
  await waitFor(() =>
    expect(screen.getByLabelText("Daily job name")).toHaveValue(""),
  );
  expect(submissions).toHaveLength(1);
  expect(submissions[0]?.get("endDate")).toBe("");
});

it("empties the end date when the form resets after creating a job", async () => {
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <RecurringJobForm children={children} today="2026-10-15" />,
        action: async () => ({ intent: "addRecurring", queued: true }),
      },
    ],
    { initialEntries: ["/"] },
  );
  const user = userEvent.setup();
  render(<RouterProvider router={router} />);

  await user.click(screen.getByText("Routines"));
  await user.type(screen.getByLabelText("Daily job name"), "Feed the fish");
  await user.type(
    screen.getByLabelText("Daily job ends (optional)"),
    "2026-11-30",
  );
  await user.click(screen.getByRole("button", { name: "Create daily job" }));

  await waitFor(() =>
    expect(screen.getByLabelText("Daily job name")).toHaveValue(""),
  );
  expect(screen.getByLabelText("Daily job ends (optional)")).toHaveValue("");
  expect(
    screen.queryByRole("button", { name: "Clear end date" }),
  ).not.toBeInTheDocument();
});
