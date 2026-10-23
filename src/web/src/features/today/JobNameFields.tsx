import type { TodayJob } from "../../api/today";

/** Names the undo toast uses to describe what happened to this job. */
export function JobNameFields({
  job,
}: {
  job: Pick<TodayJob, "name" | "childDisplayName">;
}) {
  return (
    <>
      <input type="hidden" name="jobName" value={job.name} />
      <input type="hidden" name="childName" value={job.childDisplayName} />
    </>
  );
}
