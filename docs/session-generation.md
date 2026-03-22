# How Session Generation Works

A background service (`SessionGeneratorService`) runs immediately on startup and again every 24 hours. It calls `ScheduleService.GenerateAllSessionsAsync()` which:

1. Loads each active net and its `NetScheduleRule` entries (which days of the week it runs)
2. Iterates over the next 9 weeks, creating a `NetSession` record for each matching day
3. Respects the net's season window if configured (e.g., a net that only runs May–September)
4. Skips dates that already have a session — the generator is safe to run repeatedly

The public schedule and coordinator calendar always read from these pre-generated sessions.

## EF Migrations

```bash
# Add a new migration
dotnet ef migrations add MigrationName --project NcsScheduler/NcsScheduler.csproj

# Apply migrations manually (the app also applies them automatically on startup)
dotnet ef database update --project NcsScheduler/NcsScheduler.csproj
```
