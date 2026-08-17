# CONTEXT.md

# Family Jobs Board - Domain Model

## Core Concepts

### User
A person who uses the application. A User can be either a Child or an Adult.

#### Characteristics
- ID (GUID)
- Name (string)
- Surname (string)
- IsAdult (bool)
- Pin (4 or 6 digit number, hashed)

### Child
A user who is a minor within the family system. Children can view assigned jobs and accumulate points.

#### Characteristics
- All User characteristics
- Points accumulated from completed jobs and good behaviours

### Adult  
A user with administrative privileges within the family system. Adults can manage users, jobs, and good behaviours, and approve job completions.

#### Characteristics
- All User characteristics
- Full administrative capabilities including user management, job creation, good behaviour logging, and point adjustments

### Job
A task or chore assigned to a specific user within the family system. 

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

### Good Behaviour
A positive action observed and logged by an Adult that earns points for a Child.

#### Characteristics
- ID (GUID)  
- Type (GUID, linked to Good Behaviour Type)
- Points (whole number)
- Logged Date and Time

### Good Behaviour Type
Pre-defined categories of good behaviours that can be logged.

#### Characteristics
- ID (GUID)
- Name (Text)
- Description (free text)
- Points

### Points
The currency accumulated by Children for completing Jobs and good behaviours. Points can be redeemed by Adults for rewards.

#### Properties
- Accumulated total per Child
- Trackable history of point assignments
- Can be adjusted by Adults (increased or decreased)
- Do not expire

### Administration
Functions available to Adult users for managing the system.

#### Capabilities
- Manage Users (create, update, soft-delete)
- Manage Jobs (add, edit, schedule, delete)
- Manage Good Behaviours 
- Manually adjust accumulated points
- View audit trails for all actions

## Relationships

1. User → Child/Adult (is-a relationship)
2. Child → Job (assignee relationship)  
3. Adult → Job Approval (approver relationship)
4. Adult → Good Behaviour (creator relationship)
5. Child → Good Behaviour Type (can log relationship)

## Constraints

- All deletions are soft-deletions with tracking
- Audit trails maintained for all user actions
- Children have 4-digit pins, adults have 6-digit pins