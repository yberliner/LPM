# LPM Project

## Tech Stack
- ASP.NET 8 Blazor Server app with Bootstrap UI
- SQLite database: `lifepower.db` (single DB for everything)

## Conventions
- DB tables use group prefixes: `core_`, `lkp_`, `sess_`, `cs_`, `acad_`, `fin_`, `sys_`
- PersonId is the shared key across auditors, case supervisors, and PCs
- Auth: cookie-based, roles are Admin and Customer

## Rules
- NEVER commit without explicit user permission
- NEVER touch LPM.Tests unless explicitly asked
- Proceed autonomously for file edits and builds; ask only for destructive/risky actions
- Use Bootstrap for all UI components
- NEVER create DB tables from code — manage schema directly in the database
- No backward compatibility needed — just change the code, no shims or migration paths

## Build
- Solution: `LPM_Server/LPM.sln`
- Publish: linux-x64, self-contained → `C:\PublishedApps\LPM\`
