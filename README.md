# PC Ops

This repository contains automation scripts, configurations, and task definitions used to manage power and lifecycle events across supported environments.

## Repository layout

- `projects/`: deployable applications.
	- `projects/timetracker`: .NET task tracking CLI.
	- `projects/activity-watchdog`: .NET activity timer app.
	- `projects/Avatar`: distributed remote-control stack (`AvatarController`, `AvatarAgent`, and `Avatar.Shared`).
	- `projects/AvatarAgent`: legacy standalone websocket app retained for compatibility while migrating to the distributed stack.
- `automations/`: operational scripts and task-driven utilities.
	- `automations/backup-to-gdrive`: encrypted backup workflow.
	- `automations/pwr-ctrl-os`: Windows power-aware app control.
	- `automations/windows/scripts`: small Windows helper scripts.
