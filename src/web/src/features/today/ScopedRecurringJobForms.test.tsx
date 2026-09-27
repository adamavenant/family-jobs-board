import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import {
  createMemoryRouter,
  RouterProvider,
  type ActionFunctionArgs,
} from "react-router";
import { expect, it, vi } from "vitest";

import type { TodayJob } from "../../api/today";
import {
  ScopedRecurringJobCancelForm,
  ScopedRecurringJobEditForm,
} from "./ScopedRecurringJobForms";

const job: TodayJob = {
  id: "11111111-1111-4111-8111-111111111111",
  childId: "22222222-2222-4222-8222-222222222222",
  childDisplayName: "Fredster",
  name: "Empty school bags",
  description: "Put everything away.",
  points: 3,
  scheduledDate: "2026-09-28",
  agendaPeriod: "evening",
  scheduledTime: "18:00:00",
  recurringJobSeriesId: "33333333-3333-4333-8333-333333333333",
  recurrenceFrequency: "daily",
  status: "open",
  completedAtUtc: null,
  approvedAtUtc: null,
  latestRejection: null,
  recurrence: {
    seriesId: "33333333-3333-4333-8333-333333333333",
    version: 4,
    frequency: "daily",
    weekdays: [],
    dayOfMonth: null,
    startDate: "2026-09-01",
    endDate: null,
    takesTurns: false,
  },
};

const impacts = {
  thisOnly: impact(1, 0, 0),
  allFuture: impact(12, 0, 4),
  all: impact(20, 0, 8),
};

it("previews all scopes after an edit and applies the selected future scope", async () => {
  const submissions: FormData[] = [];
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <ScopedRecurringJobEditForm job={job} />,
        action: async ({ request }: ActionFunctionArgs) => {
          const form = await request.formData();
          submissions.push(form);
          return form.get("intent") === "previewRecurringJobChange"
            ? {
                intent: "previewRecurringJobChange",
                jobId: job.id,
                operation: "edit",
                previews: impacts,
              }
            : {
                intent: "applyRecurringJobChange",
                jobId: job.id,
                operation: "edit",
                success: true,
                impact: impacts.allFuture,
              };
        },
      },
    ],
    { initialEntries: ["/"] },
  );
  const user = userEvent.setup();
  render(<RouterProvider router={router} />);

  await user.click(screen.getByText("Edit job"));
  await user.selectOptions(screen.getByLabelText("Repeats"), "weekly");
  await user.click(screen.getByLabelText("Mon"));
  await user.click(screen.getByLabelText("Tue"));
  await user.click(screen.getByLabelText("Wed"));
  await user.click(screen.getByLabelText("Thu"));
  await user.click(screen.getByLabelText("Fri"));
  await user.click(screen.getByRole("button", { name: "Review changes" }));

  const scopeGroup = await screen.findByRole("group", { name: "Apply to" });
  expect(
    within(scopeGroup).getByText("12 updated · 4 cancelled"),
  ).toBeInTheDocument();
  await user.click(within(scopeGroup).getByLabelText(/All Future/));
  await user.click(
    within(scopeGroup).getByRole("button", { name: "Confirm changes" }),
  );

  expect(
    await screen.findByText(/Saved\. 12 updated · 4 cancelled/),
  ).toBeInTheDocument();
  expect(submissions).toHaveLength(2);
  expect(submissions[1]?.get("scope")).toBe("allFuture");
  expect(submissions[1]?.getAll("weekdays")).toEqual([
    "monday",
    "tuesday",
    "wednesday",
    "thursday",
    "friday",
  ]);

  await user.clear(screen.getByLabelText("Edit points"));
  await user.type(screen.getByLabelText("Edit points"), "5");
  await user.click(screen.getByRole("button", { name: "Review changes" }));
  const secondScopeGroup = await screen.findByRole("group", {
    name: "Apply to",
  });
  await user.click(within(secondScopeGroup).getByLabelText(/This Only/));
  await user.click(
    within(secondScopeGroup).getByRole("button", { name: "Confirm changes" }),
  );

  expect(submissions).toHaveLength(4);
  expect(submissions[3]?.get("requestId")).not.toBe(
    submissions[1]?.get("requestId"),
  );
});

it("uses the same three scopes for cancellation", async () => {
  const action = vi.fn(async ({ request }: ActionFunctionArgs) => {
    const form = await request.formData();
    return {
      intent: "previewRecurringJobChange",
      jobId: job.id,
      operation: "cancel",
      previews: impacts,
      submittedOperation: form.get("operation"),
    };
  });
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <ScopedRecurringJobCancelForm job={job} />,
        action,
      },
    ],
    { initialEntries: ["/"] },
  );
  const user = userEvent.setup();
  render(<RouterProvider router={router} />);

  await user.click(screen.getByText("Cancel job"));
  await user.click(screen.getByRole("button", { name: "Review changes" }));

  expect(
    await screen.findByRole("radio", { name: /This Only/ }),
  ).toBeInTheDocument();
  expect(screen.getByRole("radio", { name: /All Future/ })).toBeInTheDocument();
  expect(screen.getAllByRole("radio")).toHaveLength(3);
  expect(action).toHaveBeenCalledOnce();
});

it("requires another preview after an edited value changes", async () => {
  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <ScopedRecurringJobEditForm job={job} />,
        action: async () => ({
          intent: "previewRecurringJobChange",
          jobId: job.id,
          operation: "edit",
          previews: impacts,
        }),
      },
    ],
    { initialEntries: ["/"] },
  );
  const user = userEvent.setup();
  render(<RouterProvider router={router} />);

  await user.click(screen.getByText("Edit job"));
  await user.click(screen.getByRole("button", { name: "Review changes" }));
  expect(
    await screen.findByRole("group", { name: "Apply to" }),
  ).toBeInTheDocument();

  await user.clear(screen.getByLabelText("Edit points"));
  await user.type(screen.getByLabelText("Edit points"), "5");

  expect(
    screen.queryByRole("group", { name: "Apply to" }),
  ).not.toBeInTheDocument();
  expect(
    screen.getByRole("button", { name: "Review changes" }),
  ).toBeInTheDocument();
});

function impact(
  updatedCount: number,
  createdCount: number,
  cancelledCount: number,
) {
  return {
    updatedCount,
    createdCount,
    cancelledCount,
    approvedSkippedCount: 0,
    cancelledSkippedCount: 0,
    retrospectivePointIncreaseSkippedCount: 0,
    warnings: [],
  };
}
