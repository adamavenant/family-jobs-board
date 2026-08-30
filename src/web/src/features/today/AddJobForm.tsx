import { Form, useActionData } from "react-router";
import type { ActionFunctionArgs } from "react-router";

import { addJob } from "../../api/today";

export function AddJobForm() {
  const actionData = useActionData<{ error?: string }>();
  
  return (
    <section className="add-job">
      <h2>Add a new job</h2>
      <Form method="post" className="add-job__form" action="/api/today/jobs">
        <div className="form-group">
          <label htmlFor="name">Job name</label>
          <input
            type="text"
            id="name"
            name="name"
            required
            aria-describedby={actionData?.error ? "name-error" : undefined}
          />
        </div>

        <div className="form-group">
          <label htmlFor="description">Description</label>
          <textarea
            id="description"
            name="description"
            required
            rows={3}
            aria-describedby={actionData?.error ? "description-error" : undefined}
          />
        </div>

        <div className="form-group">
          <label htmlFor="points">Points</label>
          <input
            type="number"
            id="points"
            name="points"
            min="0"
            required
            aria-describedby={actionData?.error ? "points-error" : undefined}
          />
        </div>

        {actionData?.error && (
          <p role="alert" className="error-message" id="add-job-error">
            {actionData.error}
          </p>
        )}

        <button type="submit">Add job</button>
      </Form>
    </section>
  );
}

export async function action({ request }: ActionFunctionArgs) {
  const formData = await request.formData();
  const name = formData.get("name") as string;
  const description = formData.get("description") as string;
  const points = Number(formData.get("points"));

  try {
    // Call the API to add the job
    await addJob({ name, description, points });
    
    // Return success - this will cause revalidation of the loader
    return { success: true };
  } catch (error) {
    console.error("Failed to add job", error);
    return { error: "Failed to add job. Please check the details and try again." };
  }
}