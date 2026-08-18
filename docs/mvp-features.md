## User

A user has a first name and a last name. 
A user can be an Adult or a Child. 

### Model
ID (GUID)
Name (string)
Surname (string)
IsAdult (bool)
Pin (4 or 6 digit number, hashed)

## User Roles
### Adult
- Can administer Users. 
- Can administer Jobs (Add, Edit properties and schedule, )
- Can approve or reject a Job completed by a Child
- Can administer Good Behaviours
- Can log a Good Behaviour
- Can redeem Points accumulated for Jobs and Good Behaviours.
- Can view current day's jobs for all children, filterable by child. 
- Can view pending job approvals. 
- Can view calendar, with day, month and week views. 

### Child
- Can view jobs assigned to them. 
- Can select a job and complete it.
- Can view points they have earned
- Can view Good Behaviours

## Job
A job is a task or chore. Currently, a job has a scheduled date and (optional) time. The description is a free-text field to provide more context on the job. A job is assigned to a user. The assigned user can mark the job as complete. A job has a points value and those points are awarded to the assigned user once a job is completed and approved. 

A job can exist as a once-off, or be scheduled to repeat (daily, weekly, monthly). 
### Model
ID (GUID)
Name (Text)
Description (Large Text)
Points (whole number)
Type (once off, recurring)
Scheduled date (date)
Scheduled time (optional, time)
Completed (bool)
Completed date and time (captured on completion)

### Approval Flow
When a child marks a job as complete, it needs to be approved by an adult before the points are awarded. When a job is completed it goes into an approval queue that is accessible by all adult users. 

When the completed job is selected from the queue, it can be approved or rejected. In both cases, a reason (free-text) can optionally be included. The point value of the job can be adjusted before approval. 

It is not necessary to re-enter the parent pin when approving or rejecting completed jobs. 
## Good Behaviour
Adults can log a good behaviour in the app when it is observed. The Good Behaviour 
has a point score that is awarded to the assigned user when it is logged. When a new Good Behaviour is logged, it should be selected from a pre-populated list e.g. Showing Kindness, Being Helpful, Being Brave. Each good behaviour has a point value. When a good behaviour is logged, the number of points for the good behaviour can be edited. 

### Model - Good Behaviour Type
ID (GUID)
Name (Text)
Description (free text)
Points
### Model - Good Behaviour
ID (GUID)
Type (GUID, linked to Good Behaviour Type)
Points (whole number)
Logged Date and Time

## Points
Points are assigned to a child user for completed (and approved) jobs and good behaviours. Points accumulate until they are redeemed by an adult. They do not expire. A user is able to view the points they have accumulated, along with details of how they were earned. 

A child's accumulated points can be redeemed by an adult at any time. Once redeemed, points will no longer show in the accumulated total. When redeeming points, a free-text field should be included to capture details of what they were redeemed for. 

An adult can edit the accumulated points of a child to increase or decrease them. These changes should be captured by the application. 

## Administration
The application should provide functionality to administer (create, update and delete) the following: 
- Users
- Jobs
- Good Behaviours
- Manually adjust accumulated points. 

Audit trails should be maintained for all user actions in the application. 
Any deletions in the app should be 'soft', using a deletion date, time and user. 
