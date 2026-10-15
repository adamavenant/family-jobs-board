import type { TodayBoard, TodayJob } from "../../api/today";
import { formText } from "../../app/actionDescriptions";
import type { QueuedAction } from "../../app/undoQueue";

export interface ShownJob {
  job: TodayJob;
  /** A queued action changed this job; it can't be acted on again until saved. */
  queued: boolean;
  /** A queued "add" that the server hasn't created yet. */
  adding: boolean;
}

/**
 * The board as it will look once every queued action is saved: completed and
 * approved jobs show their new state, cancelled or moved jobs leave the day,
 * edits show their new details, and queued additions appear as placeholders.
 */
export function showQueuedJobChanges(
  board: TodayBoard,
  actions: readonly QueuedAction[],
): { jobs: ShownJob[]; pendingApprovalCount: number } {
  const jobs: ShownJob[] = [];
  for (const job of board.jobs) {
    const form = actions.find(
      (action) => action.formData.get("jobId") === job.id,
    )?.formData;
    if (!form) {
      jobs.push({ job, queued: false, adding: false });
      continue;
    }

    const changed = applyQueuedChange(job, form);
    const movedAway =
      changed !== null &&
      changed.scheduledDate !== job.scheduledDate &&
      changed.scheduledDate !== board.date;
    if (changed && !movedAway) {
      jobs.push({ job: changed, queued: true, adding: false });
    }
  }

  for (const action of actions) {
    const form = action.formData;
    if (
      form.get("intent") !== "add" ||
      formText(form, "scheduledDate") !== board.date
    ) {
      continue;
    }

    for (const childId of form.getAll("childIds")) {
      const child = board.members.find((member) => member.id === childId);
      if (
        !child ||
        (board.selectedChildId !== null && board.selectedChildId !== childId)
      ) {
        continue;
      }

      jobs.push({
        job: {
          id: `${action.id}-${child.id}`,
          childId: child.id,
          childDisplayName: child.displayName,
          name: formText(form, "name").trim(),
          description: formText(form, "description").trim(),
          points: Number(formText(form, "points")),
          scheduledDate: board.date,
          agendaPeriod: agendaPeriod(form, "unscheduled"),
          scheduledTime: formText(form, "scheduledTime") || null,
          recurringJobSeriesId: null,
          recurrenceFrequency: null,
          status: "open",
          completedAtUtc: null,
          approvedAtUtc: null,
          latestRejection: null,
        },
        queued: true,
        adding: true,
      });
    }
  }

  const pendingBefore = board.jobs.filter(
    (job) => job.status === "pendingApproval",
  ).length;
  const pendingAfter = jobs.filter(
    ({ job, adding }) => !adding && job.status === "pendingApproval",
  ).length;
  return {
    jobs,
    pendingApprovalCount: Math.max(
      0,
      board.pendingApprovalCount - pendingBefore + pendingAfter,
    ),
  };
}

function applyQueuedChange(job: TodayJob, form: FormData): TodayJob | null {
  switch (form.get("intent")) {
    case "complete":
      // A grown-up's confirmed completion is approved at once.
      return {
        ...job,
        status: form.has("expectedPoints") ? "approved" : "pendingApproval",
      };
    case "approve":
      return { ...job, status: "approved" };
    case "reject":
      return {
        ...job,
        status: "open",
        latestRejection: {
          decisionId: "queued",
          reason: formText(form, "reason").trim() || null,
          rejectedAtUtc: "",
        },
      };
    case "cancelJob":
      return null;
    case "editJob":
      return withEdits(job, form, true);
    case "applyRecurringJobChange":
      return form.get("operation") === "cancel"
        ? null
        : withEdits(job, form, form.get("scope") === "thisOnly");
    default:
      return job;
  }
}

function withEdits(job: TodayJob, form: FormData, canMove: boolean): TodayJob {
  const points = Number(formText(form, "points"));
  const frequency = form.get("frequency");
  return {
    ...job,
    name: formText(form, "name").trim() || job.name,
    description: formText(form, "description").trim(),
    points: Number.isInteger(points) ? points : job.points,
    scheduledDate: canMove
      ? formText(form, "scheduledDate") || job.scheduledDate
      : job.scheduledDate,
    agendaPeriod: agendaPeriod(form, job.agendaPeriod),
    scheduledTime: formText(form, "scheduledTime") || null,
    recurrenceFrequency:
      job.recurringJobSeriesId &&
      (frequency === "daily" ||
        frequency === "weekly" ||
        frequency === "monthly")
        ? frequency
        : job.recurrenceFrequency,
  };
}

function agendaPeriod(
  form: FormData,
  fallback: TodayJob["agendaPeriod"],
): TodayJob["agendaPeriod"] {
  const value = form.get("agendaPeriod");
  return value === "morning" ||
    value === "arrivingHome" ||
    value === "evening" ||
    value === "unscheduled"
    ? value
    : fallback;
}
