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

  return {
    jobs,
    pendingApprovalCount: queuedPendingApprovalCount(
      board.pendingApprovalCount,
      board.jobs,
      actions,
    ),
  };
}

/**
 * One job as it will look once its queued action is saved: `null` when the action removes it
 * (cancelling), and `queued` while that action waits behind its undo toast.
 */
export function showQueuedJob(
  job: TodayJob,
  actions: readonly QueuedAction[],
): { job: TodayJob | null; queued: boolean } {
  const form = actions.find(
    (action) => action.formData.get("jobId") === job.id,
  )?.formData;
  return form
    ? { job: applyQueuedChange(job, form), queued: true }
    : { job, queued: false };
}

/**
 * The household's "awaiting review" count once every queued action is saved. The count spans
 * every date, but a page holds only some jobs: those it holds are counted by their new state,
 * and an approval or rejection queued for any other job (from the approvals panel or the
 * calendar) always decides one that was waiting.
 */
export function queuedPendingApprovalCount(
  count: number,
  jobs: readonly TodayJob[],
  actions: readonly QueuedAction[],
): number {
  let pending = count;
  for (const job of jobs) {
    const shown = showQueuedJob(job, actions).job;
    pending +=
      Number(shown?.status === "pendingApproval") -
      Number(job.status === "pendingApproval");
  }

  const held = new Set(jobs.map((job) => job.id));
  pending -= actions.filter((action) => {
    const intent = action.formData.get("intent");
    const jobId = action.formData.get("jobId");
    return (
      (intent === "approve" || intent === "reject") &&
      typeof jobId === "string" &&
      !held.has(jobId)
    );
  }).length;
  return Math.max(0, pending);
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
    case "reverseApproval":
      // A grown-up's own completion is approved at the moment it is completed, and
      // reversing it reopens the job; a child's completion goes back for review.
      return job.completedAtUtc !== null &&
        job.completedAtUtc === job.approvedAtUtc
        ? { ...job, status: "open", completedAtUtc: null, approvedAtUtc: null }
        : { ...job, status: "pendingApproval", approvedAtUtc: null };
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
