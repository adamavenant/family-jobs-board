# 0004 - Undo actions by delaying their saves in the browser

## Status

Accepted

## Context

Issue #139 asks for an Undo toast on every household action, for a duration
grown-ups set in the app. Undoing after a save would need a reversal endpoint
and compensating rules for every action, each with its own conflicts (points
already spent, jobs already reviewed). The household chose to delay saving
instead.

## Decision

- **Delay, don't reverse.** A route action validates the form as before, then
  queues its API call behind an undo toast and returns at once with
  `queued: true`. The call runs when the toast expires or is dismissed; Undo
  drops it without a request. No server endpoint knows about undo.
- **One queue in the browser, outside React state.** The queue lives in
  `src/web/src/app/undoQueue.ts` as module state with a
  `useSyncExternalStore` hook, alongside the in-memory session in `auth.ts`.
  Context alone would not do: React Router actions run outside the React tree
  and must queue into it, and queued work must outlive the page that started it
  (navigating from Today to Calendar must not drop or send it). A root layout
  route renders the toast stack, connects the router's `revalidate`, and sends
  queued work on `visibilitychange`/`pagehide`. Identity changes (sign out,
  sign in, PIN setup) send queued work first.
- **Keep the session alive and the refresh single.** Because a queued
  action sends nothing until it is saved, the browser refreshes the session
  once at queue time when the server's inactivity limit would otherwise pass
  first, and concurrent refreshes share one request so saving several actions
  together never reuses the rotating refresh cookie (ADR 0003).
- **Revalidate after the save, not the submission.** Routes skip revalidation
  for `queued` results (`revalidateUnlessQueued`), so nothing is fetched until
  the queue has saved; the queue then revalidates whether the save succeeded or
  failed, so the page shows the server's state.
- **Optimistic state from queued forms.** Pages read queued `FormData` (like
  `fetcher.formData`) to show the after-action state and lock the item.
  Panels that list data load it through a dedicated fetcher, so lists refresh
  when the queue revalidates.
- **A household setting.** The undo time (5–120 seconds, default 30) is stored
  server-side in a one-row `household_settings` table, read by any member,
  changed only by adults, and included in the Today board so devices pick up
  changes on their next load.

## Consequences

### Positive

1. **Uniform:** wrapping an action is one call in its route action; new
   actions get undo by posting to an existing intent (the approvals panel and
   calendar decisions) or by wrapping their own (reversing an approval).
2. **No reversal semantics:** the server never sees undone actions, so ledgers
   and audit history contain only real changes.
3. **Idempotent retries:** a refused save can be sent again with the same
   request, including its request ID.

### Negative

1. **Delay:** other devices see a change only after the undo time.
2. **Late server errors:** rules only the server can check (stale or
   insufficient points, conflicts) fail after the undo time, in an error toast,
   and cleared form entries are not restored.
3. **Loss on abrupt shutdown:** a queued action can be lost if the device dies
   before `pagehide` lets it send.
4. **Global client state:** the queue is module state, which tests must reset
   between cases.

### Neutral

1. **Server-side scope:** only the setting and its board field are new on the
   server; every existing endpoint is unchanged.
