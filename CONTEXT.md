# CONTEXT.md

# Family Jobs Board - Domain Model

## Core Concepts

### User
A person who uses the application. A User can be either a Child or an Adult.

#### Characteristics
- ID (GUID)
- First name (string)
- Surname (string, required before sign-in is enabled)
- Nickname (optional string)
- Role (`Adult` or `Child`)
- PIN credential state (`NotSet` or `Ready`)

### Child
A user who is a minor within the family system. Children can view assigned jobs and accumulate points.

#### Characteristics
- All User characteristics
- Points accumulated from completed jobs and good behaviours (integer, positive only)

### Adult  
A user with administrative privileges within the family system. Adults can manage users, jobs, and good behaviours, and approve job completions.

#### Characteristics
- All User characteristics
- Implemented administration includes user management, job creation,
  good-behaviour types and logging, manual point adjustments, and reward
  redemptions

### Job
A task or chore assigned to one child within the family system. An adult may
assign the same details to multiple children in one action; this creates one
independent Job per child so completion, review, and points remain separate.

#### Characteristics
- ID (GUID)
- Name (Text)
- Description (Large Text)
- Points (whole number)
- Type (once off, recurring)
- Scheduled date (date)
- Scheduled time (optional, time)
- Completed (bool)
- Completed date and time (captured on completion)

### Take-turns Schedule
A recurring job shared by two or more children who take turns, such as tidying
the table. It is one recurring series with an ordered rotation of children;
each occurrence is an ordinary Job assigned to the next child in the rotation
(alternating for two children, round robin for more). The adult picks who goes
first. The rotation advances on every occurrence, even one that is later
cancelled or rejected, so upcoming turns are predictable.

### Job Approval
The process by which an Adult verifies that a Child has completed a Job before points are awarded.

#### Process
1. Child marks job as complete
2. Job enters approval queue visible to all Adults
3. Adult reviews pending jobs in queue
4. Adult approves or rejects job completion
5. Points awarded if approved, no points if rejected

An Adult may also mark a Child's open job done on the Child's behalf, which
approves it and awards its points in one step.

#### Reversing an approval
An Adult can reverse an approval at any time, for example after approving a job
they meant to cancel. The job returns to how it was before it was approved:
waiting for approval, with its completion time, if the Child submitted it; open,
with no completion, if an Adult marked it done for the Child. A new negative
points entry takes the award back; the award itself stays in the history. The
reversal is recorded as a review decision, and the job can then be approved
again (awarding its points once more), rejected, or cancelled. It is refused if
the Child no longer has the points, for example because they were redeemed.

### Good Behaviour
A positive action observed and logged by an Adult that earns points for a Child
immediately. Points default to the type's points and can be changed when
logging. An Adult may log the same behaviour for several children in one action;
this creates one independent Good Behaviour (and points award) per child. Only
Adults can log; Children see the result in their points history.

#### Characteristics
- ID (GUID)  
- Type (GUID, linked to Good Behaviour Type)
- Child and logging Adult
- Points awarded (whole number, editable at logging time)
- Name and description of the type as they were when logged
- Logged Date and Time

### Good Behaviour Type
A pre-defined category of good behaviour that Adults can log, such as Showing
Kindness. Adults create, edit, and soft-delete types; Children can view the
active ones and their usual points but cannot change them. Editing or deleting
a type never changes behaviours already logged.

#### Characteristics
- ID (GUID)
- Name (Text)
- Description (free text)
- Points (usual amount awarded)
- Active flag with audit of who created, edited, and deleted it

### Points
The currency accumulated by Children. Job-earned awards and their approval
reversals, good-behaviour awards, manual adjustments, and adult redemptions, with
their append-only history, are implemented.

#### Properties
- Accumulated total per Child
- Trackable history of point assignments, read as a newest-first points
  ledger with a running balance: each Child sees only their own, and Adults
  see every Child's, filterable by Child. Each entry names the Adult who
  recorded it, where one is recorded
- Never negative: points are never taken below zero, and every removal for a
  Child (an adjustment, a redemption, or an approval reversal) is serialized so
  concurrent removals can't overdraw a balance
- Each approval of a Job awards its points once. A Job has at most one award
  that hasn't been reversed
- Manual adjustments by Adults are signed and need a reason; mistakes are
  corrected with a new opposite entry, never by editing history
- Redemptions: an Adult spends a Child's points on a free-text reward; a
  redemption is its own negative ledger entry and is never edited or undone
- Do not expire

### Turn Rotation ("Whose Turn Is It?")
An informational household rota, independent of jobs, completions, approvals,
and points, and distinct from take-turns recurring-job assignments. It answers
a standing household question (default "Who is Pink today?") by cycling
through an ordered list of children. Each rotation moves on in one of two
ways (its advance mode): every calendar day, with no interaction, or only
when the current child's turn is ticked off on the board — for turns that
don't happen every day, such as "Who is first in the shower?" when showers
are every other day.

#### Characteristics
- A household can run any number of independent rotations (for example "Who
  is Pink today?" and "Who sits next to Mum?"). Each is an ordered,
  append-only history of effective-dated revisions; existing revisions are
  never edited, and a rotation is ended by a participant-less revision.
- Each revision has an effective date, a question, an ordered list of
  distinct active-child participants, which participant is assigned on the
  effective date, and the creating Adult and UTC creation time.
- Each revision has an advance mode: every day (the default, and the mode of
  every rotation saved before tick-to-advance existed) or when ticked.
- In a daily revision, the assignee for any date is the number of days since
  the revision's effective date, modulo the participant count, starting from
  the configured first child; every calendar day counts and the calculation
  needs no background job.
- In a when-ticked revision, the turn starts with the first child and moves to
  the next participant once per Taken Turn recorded against that revision.
  With two children a tick passes the turn straight to the other child the
  same day; with one, or three or more, the ticked child shows as done for
  the rest of the day and the next child is up from the following day, so a
  rotation can be ticked at most once a day. A past date shows whose turn it
  was at the start of that day and whether a turn was ticked; a future date
  shows whose turn it is now.
- A rotation's first revision may take effect today; every later revision of
  it takes effect no earlier than the next household-local day, so reconfiguring
  never changes an answer that already happened.

### Taken Turn
A tick recording that a child took their turn in a when-ticked Turn Rotation.
Any Adult, or the child whose turn it is, can tick it, and only for today
(household-local). A tick names the turn it takes — the revision and the
turn's number within it, as well as the child — so a stale tick is refused
even when the same child's turn has come round again, and it carries a
request ID so a replayed request returns the original tick instead of ticking
again. The most recent tick can be unticked the
same day by an Adult or the child whose turn was ticked. Ticks are never
deleted: unticking marks one undone with who and when, and an undone tick no
longer counts.

#### Characteristics
- ID (GUID) and request ID (GUID, unique)
- Rotation, the revision it counts towards, and the turn number it took
- Household-local date and the child whose turn was taken
- Recording member and UTC time
- Unticking member and UTC time, when unticked

### Household Settings
Preferences that every device in the household shares. Today it holds the undo
time: how long a household action — completing, approving, or rejecting a job,
changing jobs, points, behaviours, family members, or rotations, or ticking a
turn — waits with
an Undo toast before it is saved.

#### Characteristics
- Undo time in whole seconds from 5 to 120; a new household uses 30
- Adults change it; Children can read it but not change it
- Records which Adult last changed it and when
- An undone action never reaches the server, so history records only real
  changes

### Administration
Functions available to Adult users for managing the system.

#### Capabilities
- Manage Users (create, update, soft-delete)
- Manage Jobs (add, edit, schedule, delete)
- Manage Good Behaviours (planned, not yet implemented)
- Manually adjust accumulated points (add or remove, with a required reason)
- Redeem a Child's points for a reward
- View audit trails for all actions (planned, not yet implemented)

## Relationships

1. User → Child/Adult (is-a relationship)
2. Child → Job (assignee relationship)  
3. Adult → Job Approval (approver relationship)
4. Adult → Good Behaviour (planned creator relationship; not yet implemented)
5. Child → Good Behaviour Type (planned relationship; not yet implemented)
6. Adult → Turn Rotation revision (creator relationship)
7. Child → Turn Rotation participant (ordered membership relationship)
8. Turn Rotation revision → Taken Turn (a when-ticked revision counts its ticks)
9. Adult or Child → Taken Turn (recorder and unticker relationships)

## Constraints

- All deletions are soft-deletions with tracking
- Audit trails maintained for all user actions
- A job or recurring schedule assignment targets one or more distinct, active
  children and persists all child-specific copies atomically.
- A take-turns schedule needs at least two distinct, active children and
  creates a single series rather than one copy per child.
- Children have 4-digit PINs; adults have 6-digit PINs.
- PINs are strings so leading zeroes are retained. Only a slow, salted,
  peppered hash is stored; plaintext PINs are never persisted or logged.
- Authentication uses short-lived access tokens backed by revocable server-side
  sessions with a ten-minute inactivity timeout.
