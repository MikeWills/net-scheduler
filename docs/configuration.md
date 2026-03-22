# Configuration Reference

## `appsettings.json` — committed, no secrets

```json
{
  "App": {
    "BaseUrl": ""
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=ncsscheduler.db"
  },
  "Email": {
    "SmtpHost": "smtp.mailgun.org",
    "SmtpPort": 587,
    "Username": "",
    "Password": "",
    "FromAddress": "noreply@yourdomain.com",
    "FromName": "NCS Scheduler",
    "UseSsl": true
  }
}
```

| Key | Purpose |
|-----|---------|
| `App:BaseUrl` | Public base URL used when generating absolute links (e.g. iCal feed URLs). Leave blank in development — the app will derive the URL from the incoming request. Set to your production domain (e.g. `https://ncs.example.com`) when deploying. |

## `appsettings.Development.json` — local only, not committed to source control

```json
{
  "SeedAdmin": {
    "Email": "admin@example.com",
    "Password": "ChangeMe123!"
  }
}
```

## User Secrets — local development credential store

```bash
dotnet user-secrets set "Email:Username" "smtp-username"  --project NcsScheduler/NcsScheduler.csproj
dotnet user-secrets set "Email:Password" "smtp-password"  --project NcsScheduler/NcsScheduler.csproj
dotnet user-secrets list                                   --project NcsScheduler/NcsScheduler.csproj
```

Secrets are stored in `%APPDATA%\Microsoft\UserSecrets\` (Windows) or `~/.microsoft/usersecrets/` (Linux/macOS) — never inside the repo.

## Production — environment variables

Set these on your server; no file editing needed:

```bash
App__BaseUrl=https://ncs.example.com
Email__Username=your-smtp-username
Email__Password=your-smtp-password
ConnectionStrings__DefaultConnection=Data Source=/var/data/ncsscheduler.db
ASPNETCORE_ENVIRONMENT=Production
```

ASP.NET Core maps `__` (double-underscore) to `:` in config keys, so `App__BaseUrl` becomes `App:BaseUrl` and `Email__Password` becomes `Email:Password`.

> **systemd quoting:** Values containing spaces (like `Data Source=...`) must have the entire `KEY=VALUE` pair wrapped in outer quotes in the service file:
> ```ini
> Environment="ConnectionStrings__DefaultConnection=Data Source=/opt/ncsscheduler/ncsscheduler.db"
> ```
> Using inner quotes (`Environment=KEY="value with spaces"`) passes the literal quote characters to the app and causes a startup crash.
