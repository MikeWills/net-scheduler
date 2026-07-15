# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

NcsScheduler is an ASP.NET Core MVC web application targeting .NET 10.0. The solution file is `NcsScheduler.slnx` (the new XML solution format). Built for the OMISS amateur radio club to manage net control station scheduling.

## Commands

```bash
# Run (from repo root)
dotnet run --project NcsScheduler/NcsScheduler.csproj

# Build
dotnet build

# Watch mode (auto-reload on file changes)
dotnet watch --project NcsScheduler/NcsScheduler.csproj

# Publish (Release, targeting Linux server)
dotnet publish NcsScheduler/NcsScheduler.csproj -c Release -o publish
```

The app runs on `http://localhost:5106` (HTTP) or `https://localhost:7042` (HTTPS) in development.

## Architecture

Standard ASP.NET Core MVC layout inside the `NcsScheduler/` project:

- `Program.cs` — app startup, middleware pipeline, forwarded headers for reverse proxy
- `Controllers/` — MVC controllers
- `Models/` — view models and domain models
- `Views/` — Razor views (`.cshtml`), with `Shared/` for layout and partials
- `Services/` — business logic (ScheduleService, HolidayService, EmailService, SessionGeneratorService)
- `Helpers/` — static helpers (`DateConverter` for UTC↔Eastern conversion, `BandHelper` for band sort/display)
- `wwwroot/` — static assets (CSS, JS, libs via libman)
- `appsettings.json` / `appsettings.Development.json` — configuration

## Stack

- .NET 10.0, C# with nullable reference types and implicit usings enabled
- Bootstrap 5, jQuery, jquery-validation (bundled in `wwwroot/lib/`)
- Tom Select (`.nc-select` dropdowns) and Google Fonts (Barlow Condensed, Source Sans 3, JetBrains Mono) — both loaded via CDN in `_Layout.cshtml`, not bundled locally
- SQLite via Entity Framework Core 10
- MailKit for SMTP email
- ASP.NET Core Identity for authentication
- Deployed to Linux (Ubuntu) behind Apache reverse proxy

## Deployment

Full setup details (Tailscale, secrets, systemd, Apache) live in `docs/deployment.md`. Quick reference:

- Production server: Ubuntu Linux at `/opt/ncsscheduler/`
- Runs as systemd service (`ncsscheduler.service`) under `www-data`
- Apache reverse proxy with `ProxyPreserveHost On`, SSL via Let's Encrypt
- Production URL: `https://ncs.wx0mik.radio`
- `appsettings.json` and `appsettings.Development.json` are excluded from publish output (`CopyToPublishDirectory=Never`) — server maintains its own config
- Sensitive config (BaseUrl, email credentials, DB path) set via environment variables in the systemd unit file

### Publishing to production (GitHub flow)

`.github/workflows/deploy.yml` deploys automatically whenever a **GitHub Release is published**. Merging to `master` alone does not deploy (it only runs `dotnet.yml`'s build), and pushing a bare tag does not deploy either — a Release has to actually be published from that tag.

```bash
# 1. Work on a branch, never commit straight to master
git checkout -b fix/whatever
git commit -m "..."
git push -u origin fix/whatever
gh pr create --title "..." --body "..."

# 2. Once CI (build + claude-code-review) is green, merge
gh pr merge <number> --merge --delete-branch

# 3. Tag the commit to ship (lightweight tags; no strict version scheme in
#    use yet — check `git tag -l` for the last one before picking the next)
git tag v2.2
git push origin v2.2

# 4. Publish a GitHub Release from that tag -- THIS is the actual deploy trigger
gh release create v2.2 --title "v2.2" --notes "..."
```

Watch the deploy with `gh run list --workflow=deploy.yml` / `gh run view <id>`. The job builds, joins the server's Tailscale network, backs up the SQLite DB, stops the service, rsyncs the publish output, restarts the service, and health-checks `http://localhost:5000/` — the production port set by `ASPNETCORE_URLS` in the systemd unit. Note this is **not** 5106; that's only the dev-time port from `launchSettings.json`, which doesn't apply when systemd runs the published DLL directly.

- Manual fallback (bypasses the pipeline entirely): `dotnet publish NcsScheduler/NcsScheduler.csproj -c Release -o publish`, then copy `publish/` to the server via WinSCP or the Visual Studio FolderProfile.

## Key Data Model Notes

- `Net.ScheduledTimeUtc` and `NetSession.ScheduledTimeUtc` — stored in UTC
- `NetSession.SessionDate` — the UTC calendar date of the session
- `NetScheduleRule.DayOfWeek` — UTC day of week (used for session generation)
- `StandingAssignment.DayOfWeek` — **Eastern local** day of week (requires conversion when matching against UTC dates)
- `Helpers/DateConverter` is the public UTC↔Eastern helper — `ToEasternDate()`, `ToUtcSessionDate()`, `TodayEastern()`. Use this from controllers/views.
- **Watch out:** `ScheduleService` has its own separate `private` `ToEasternDate()`/`ToEasternTime()` plus a private Eastern-zone lookup, duplicated rather than reusing `DateConverter`. Two independent implementations of the same DST-sensitive logic is how the Eastern-date comparison bug in Unavailability validation happened — if you fix a conversion bug in one, check whether the other needs the same fix.
