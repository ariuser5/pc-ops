# PC Ops

PC Ops is a collection of machine-level tooling, automations, and small utilities for local operations. Individual projects and workflows live under the `projects/` and `automations/` directories.

This repository is general-purpose — it contains multiple tools (for example: activity-watchdog, timetracker, and various automations). One of the projects is the distributed Avatar runtime; its full documentation has been moved to `projects/Avatar/README.md`.

For project-specific documentation and usage instructions, open the README.md inside the corresponding project directory (e.g., `projects/Avatar/README.md`).

Contributing:
- Run tests: `dotnet test .\projects\Avatar\Avatar.Tests\Avatar.Tests.csproj`
- Open issues or PRs for changes to tooling and automations.

Note: Repository layout can change. Documentation should focus on purpose, operation, and usage, not on fixed file-tree diagrams.