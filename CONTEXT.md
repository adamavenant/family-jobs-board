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
- Implemented administration includes user management and job creation;
  good-behaviour logging and point adjustments are planned, not yet implemented

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

### Job Approval
The process by which an Adult verifies that a Child has completed a Job before points are awarded.

#### Process
1. Child marks job as complete
2. Job enters approval queue visible to all Adults
3. Adult reviews pending jobs in queue
4. Adult approves or rejects job completion
5. Points awarded if approved, no points if rejected

### Good Behaviour (planned, not yet implemented)
A planned positive action observed and logged by an Adult that earns points for a Child.

#### Characteristics
- ID (GUID)  
- Type (GUID, linked to Good Behaviour Type)
- Points (whole number)
- Logged Date and Time

### Good Behaviour Type (planned, not yet implemented)
Planned pre-defined categories of good behaviours that can be logged.

#### Characteristics
- ID (GUID)
- Name (Text)
- Description (free text)
- Points

### Points
The currency accumulated by Children. Job-earned awards and their append-only
history are implemented. Good-behaviour awards and adult redemption or manual
adjustment are planned and are not yet implemented.

#### Properties
- Accumulated total per Child
- Trackable history of point assignments
- Adult redemption and manual adjustment are planned, not yet implemented
- Do not expire

### Administration
Functions available to Adult users for managing the system.

#### Capabilities
- Manage Users (create, update, soft-delete)
- Manage Jobs (add, edit, schedule, delete)
- Manage Good Behaviours (planned, not yet implemented)
- Manually adjust accumulated points (planned, not yet implemented)
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
- Children have 4-digit PINs; adults have 6-digit PINs.
- PINs are strings so leading zeroes are retained. Only a slow, salted,
  peppered hash is stored; plaintext PINs are never persisted or logged.
- Authentication uses short-lived access tokens backed by revocable server-side
  sessions with a ten-minute inactivity timeout.
