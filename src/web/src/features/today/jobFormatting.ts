import type { TodayJob } from "../../api/today";

export function formatAgendaPeriod(value: TodayJob["agendaPeriod"]) {
  return value === "arrivingHome"
    ? "Arriving home"
    : value === "unscheduled"
      ? "Any time"
      : `${value.charAt(0).toUpperCase()}${value.slice(1)}`;
}

export function formatRecurrenceFrequency(
  value: TodayJob["recurrenceFrequency"],
): string {
  if (value === "weekly") {
    return "Weekly";
  }
  if (value === "monthly") {
    return "Monthly";
  }

  return "Daily";
}

export function jobStatusLabel(status: TodayJob["status"]): string {
  if (status === "approved") {
    return "Points awarded";
  }
  if (status === "pendingApproval") {
    return "Waiting for approval";
  }
  if (status === "cancelled") {
    return "Cancelled";
  }

  return "Ready to do";
}

export function jobStatusClassName(status: TodayJob["status"]): string {
  const modifier =
    status === "approved"
      ? "approved"
      : status === "pendingApproval"
        ? "pending"
        : status === "cancelled"
          ? "cancelled"
          : "open";
  return `status status--${modifier}`;
}

export function addDays(date: string, days: number): string {
  const value = new Date(`${date}T12:00:00Z`);
  value.setUTCDate(value.getUTCDate() + days);
  return value.toISOString().slice(0, 10);
}

export function addMonths(date: string, months: number): string {
  const [year, month] = date.split("-").map(Number);
  const shifted = new Date(Date.UTC(year!, month! - 1 + months, 1));
  return shifted.toISOString().slice(0, 10);
}

export function boardHref(
  date: string,
  currentDate: string,
  selectedChildId: string | null,
): string {
  const search = new URLSearchParams();
  if (date !== currentDate) {
    search.set("date", date);
  }
  if (selectedChildId) {
    search.set("childId", selectedChildId);
  }
  const query = search.toString();
  return query ? `/?${query}` : "/";
}
