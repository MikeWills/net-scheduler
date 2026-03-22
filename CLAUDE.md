# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

NcsScheduler is an ASP.NET Core MVC web application targeting .NET 8.0. The solution file is `NcsScheduler.slnx` (the new XML solution format).

## Commands

```bash
# Run (from NcsScheduler/ project directory)
dotnet run --project NcsScheduler/NcsScheduler.csproj

# Build
dotnet build

# Watch mode (auto-reload on file changes)
dotnet watch --project NcsScheduler/NcsScheduler.csproj
```

The app runs on `http://localhost:5106` (HTTP) or `https://localhost:7042` (HTTPS) in development.

## Architecture

Standard ASP.NET Core MVC layout inside the `NcsScheduler/` project:

- `Program.cs` — app startup and middleware pipeline
- `Controllers/` — MVC controllers
- `Models/` — view models and domain models
- `Views/` — Razor views (`.cshtml`), with `Shared/` for layout and partials
- `wwwroot/` — static assets (CSS, JS, libs via libman)
- `appsettings.json` / `appsettings.Development.json` — configuration

The project uses `UseStaticFiles()` for static asset serving.

## Stack

- .NET 8.0, C# with nullable reference types and implicit usings enabled
- Bootstrap 5, jQuery, jquery-validation (bundled in `wwwroot/lib/`)
- SQLite via Entity Framework Core
- Deployed to Linux (Ubuntu) behind Apache reverse proxy
