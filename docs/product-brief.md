Family Jobs Board is a web app that presents a list of jobs for a given day, per user. A user can be a child or adult. Each day, jobs are presented as an agenda, arranged by time period. Time periods are in the week are Mornings (getting ready), Arriving Home, Evening (including getting ready for bed). 

Weekends would be different, in that there are less jobs. When a job is completed, it needs to be checked by an adult. If approved, the child user is awarded points. Points can be accumulated to earn rewards. In addition to jobs, Good Behaviours can be logged by a parent and points awarded. 

Running point total can be seen on the home screen (probably a SPA). Tapping on the score will show a report of where points have been earned (job, points awarded, time, approving parent). 

The app will be web-based, Docker containers that are runnable locally and can also be hosted on Kubernetes, which will initially be on a small linux server that I have in a drawer downstairs. It should be optimised for the Kindle Fire Kids tables and iPhones, though it should scale up and work on a workstation, too. 

The first screen will show available users, each selectable and triggering a pin entry. On first login, a pin must be set, and confirmed to match on a second screen. 

Jobs and good behaviours can be set up and assigned a schedule and a points value by a parent user. 

Parent pin numbers should be 6 digits long, childrens' should be 4 digits long.

There should be a function to add users (accessible by parent users only). A user consists of the following fields: 
- First Name
- Surname (not always displayed unless two users exist with the same first name)
- Pin code (encrypted)

Tech stack: C# .net back-end, react SPA front-end (using any helpful frameworks like vite or nextjs), PostGreSQL as the database, in its own container

Design should be minimalist, kid and adult friendly, uncluttered. Multiple related options should be grouped in a kebab-style menu. Think Japanese. 