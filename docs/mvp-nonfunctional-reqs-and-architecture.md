## App Flow

On app load, if there are no users, Add User functionality should be loaded. This first user must be an adult. 

On app load, if there are users, show a list of available users. Selecting a user will prompt for that user's pin number. 

If an incorrect pin is entered, inform the user and return to the user selection screen. 

If a correct pin is entered, load the jobs view. This view shows the current day and any pending tasks as an agenda view. 

For adult users, any pending job approvals should also be show. 

For child users the view also shows their currently accumulated points. Clicking on the points will open a log of how points were accumulated. 

From this view, the previous or next day can be viewed by navigating to the left or right, respectively. 

A calendar view should be available to Adults, which will show a calendar with jobs and good rewards on the relevant days. Incomplete jobs should remain pending and show on their assigned dates. 

## App Security
This application is hosted on a local network and not accessible on the internet. Therefore, security can be fairly light-touch for the MVP. Users log in using their pin code and can logout. Sessions time out after 10 minutes of inactivity. Use JWTs for authentication between the back- and front-end. 

## App Architecture
All components should run in their own Docker container. 

Back end: C# .NET 
Database: PostGreSQL
Front end: React using nextjs, vite or any other helpful tooling. 

Deploy to: a home linux server, using CI/CD. 

Deploy from: Github repo

## UI Design

Use this as visual direction only. Preferred themes:

- Minimalist
- Warm/friendly
- Kid-readable
- Parent-friendly
- Big touch targets
- Soft colors
- Low clutter
- Works well on Kindle Fire Kids tablets and iPhones

Avoid:

- Overly gamified casino-style UI
- Too many animations
- Tiny controls
- Busy dashboards