# LPM Project

## Tech Stack
- ASP.NET 8 Blazor Server app with Bootstrap UI
- SQLite database: `lifepower.db` (single DB for everything)

## Conventions
- DB tables use group prefixes: `core_`, `lkp_`, `sess_`, `cs_`, `acad_`, `fin_`, `sys_`
- PersonId is the shared key across auditors, case supervisors, and PCs
- Auth: cookie-based, roles are Admin and Customer

## Rules
- For each implementation request, first create a complete implementation plan, print it, and wait for user approval/remarks/changes before writing any code
- NEVER commit without explicit user permission
- NEVER touch LPM.Tests unless explicitly asked
- Proceed autonomously for file edits and builds; ask only for destructive/risky actions
- Prefer Bootstrap for all UI components and layout.
- Do not introduce additional UI frameworks unless explicitly requested.
- Use custom CSS or Blazor CSS isolation for small gaps instead of adding another UI library.
- NEVER create DB tables from code — manage schema directly in the database
- No backward compatibility needed — just change the code, no shims or migration paths
- All UI (new pages, modified pages, refactored components) MUST be responsive and work correctly on mobile and tablet. Use Bootstrap responsive classes and/or CSS media queries (breakpoints: 480px mobile, 768px tablet). Minimum touch target size 44px. Font sizes must be at least 16px on inputs to prevent iOS auto-zoom.

## Shorthand Commands
- **GD** = "Go Debug" — When the user writes `GD`: (1) prepare a detailed implementation plan, (2) debug the plan 5 times for logic/edge-case issues, (3) implement the code, (4) debug the written code 10 times looking for bugs and fix any found.

## Build
- Solution: `LPM_Server/LPM.sln`
- Publish: linux-x64, self-contained → `C:\PublishedApps\LPM\`
- Do NOT run `dotnet build` automatically after every change — it is slow. Only build when the user explicitly asks, or when you are already building for another reason.
- When checking build errors, grep for BOTH `error CS` (C# errors) and `error RZ` (Razor errors)
