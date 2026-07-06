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
- `Services/` — business logic (ScheduleService, EmailService, SessionGeneratorService)
- `wwwroot/` — static assets (CSS, JS, libs via libman)
- `appsettings.json` / `appsettings.Development.json` — configuration

## Stack

- .NET 10.0, C# with nullable reference types and implicit usings enabled
- Bootstrap 5, jQuery, jquery-validation (bundled in `wwwroot/lib/`)
- SQLite via Entity Framework Core 10
- MailKit for SMTP email
- ASP.NET Core Identity for authentication
- Deployed to Linux (Ubuntu) behind Apache reverse proxy

## Deployment

- Production server: Ubuntu Linux at `/opt/ncsscheduler/`
- Runs as systemd service (`ncsscheduler.service`) under `www-data`
- Apache reverse proxy with `ProxyPreserveHost On`, SSL via Let's Encrypt
- Production URL: `https://ncs.wx0mik.radio`
- `appsettings.json` and `appsettings.Development.json` are excluded from publish output (`CopyToPublishDirectory=Never`) — server maintains its own config
- Sensitive config (BaseUrl, email credentials, DB path) set via environment variables in the systemd unit file
- Publish via Visual Studio (FolderProfile) or `dotnet publish`, then copy to server via WinSCP

## Key Data Model Notes

- `Net.ScheduledTimeUtc` and `NetSession.ScheduledTimeUtc` — stored in UTC
- `NetSession.SessionDate` — the UTC calendar date of the session
- `NetScheduleRule.DayOfWeek` — UTC day of week (used for session generation)
- `StandingAssignment.DayOfWeek` — **Eastern local** day of week (requires conversion when matching against UTC dates)
- `ScheduleService` has `ToEasternDate()`/`ToEasternTime()` helpers for UTC↔Eastern conversion
