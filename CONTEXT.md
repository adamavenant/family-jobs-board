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
  good-behaviour types and logging, and manual point adjustments

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
The currency accumulated by Children. Job-earned awards, good-behaviour awards,
and manual adjustments, with their append-only history, are implemented. Adult
redemption is planned and not yet implemented.

#### Properties
- Accumulated total per Child
- Trackable history of point assignments
- Manual adjustments by Adults are signed, need a reason, and may take a balance
  negative only after explicit confirmation; mistakes are corrected with a new
  opposite entry, never by editing history
- Adult redemption is planned, not yet implemented
- Do not expire

### Administration
Functions available to Adult users for managing the system.

#### Capabilities
- Manage Users (create, update, soft-delete)
- Manage Jobs (add, edit, schedule, delete)
- Manage Good Behaviours (planned, not yet implemented)
- Manually adjust accumulated points (add or remove, with a required reason)
- View audit trails for all actions (planned, not yet implemented)

## Relationships

1. User → Child/Adult (is-a relationship)
2. Child → Job (assignee relationship)  
3. Adult → Job Approval (approver relationship)
4. Adult → Good Behaviour (planned creator relationship; not yet implemented)
5. Child → Good Behaviour Type (planned relationship; not yet implemented)

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
