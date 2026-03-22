# NCS Scheduler

A self-hosted web application for managing amateur radio net controllers. Replaces the club's Google Spreadsheet with self-service unavailability reporting, volunteer sign-up, coordinator assignment tools, and a public schedule view.

Built for OMISS 80m nets but designed to be configurable for any club running multiple nets across different days and times.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core MVC (.NET 8) |
| Database | SQLite via Entity Framework Core 8 |
| Auth | ASP.NET Core Identity |
| Email | MailKit (SMTP) |
| Frontend | Bootstrap 5, jQuery, Tom Select (searchable dropdowns) |
| Session generation | `IHostedService` background worker (runs on startup + every 24 h) |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git

No other infrastructure required — SQLite is file-based and bundled with the app.

---

## Local Development Setup

### 1. Clone and restore

```bash
git clone https://github.com/MikeWills/net-scheduler.git
cd net-scheduler
dotnet restore
```

### 2. Configure the seed admin account

Edit `NcsScheduler/appsettings.Development.json` with the email and password you want for the initial SuperAdmin account:

```json
{
  "SeedAdmin": {
    "Email": "you@example.com",
    "Password": "YourPassword123!"
  }
}
```

> **Note:** The seed admin is only created once — on first launch when no users exist. Set this before the first run, or update the account via Admin → Manage Users afterward.

### 3. Configure email credentials (User Secrets)

Email credentials are kept out of the repo using .NET User Secrets:

```bash
cd NcsScheduler
dotnet user-secrets set "Email:Username" "your-smtp-username"
dotnet user-secrets set "Email:Password" "your-smtp-password"
```

The SMTP host, port, and From address are already set in `appsettings.json` and can be changed there freely (they are not sensitive).

### 4. Run the app

```bash
dotnet run --project NcsScheduler/NcsScheduler.csproj
```

On first run the app will automatically:
1. Create `ncsscheduler.db` and apply all EF migrations
2. Seed the three roles, the SuperAdmin account, and the 3 default nets
3. Generate 9 weeks of sessions in the background

Open [http://localhost:5106](http://localhost:5106) or [https://localhost:7042](https://localhost:7042).

### 5. Watch mode (auto-reload on file changes)

```bash
dotnet watch --project NcsScheduler/NcsScheduler.csproj
```

---

## First Launch Checklist

| Step | Where |
|---|---|
| 1. Log in as SuperAdmin | Use the credentials from `SeedAdmin` in `appsettings.Development.json` |
| 2. Add net controllers | Coordinator → Net Controllers → + Add Controller |
| 3. Send account invitations | Coordinator → Net Controllers → Send Invite (per row) |
| 4. Set standing assignments | Admin → Standing Assignments |
| 5. Promote a Band Coordinator | Admin → Manage Users → Grant Coordinator, then Admin → Band Coordinators to assign nets |
| 6. Review the current week | Coordinator → Weekly Calendar |

### Seeded data on first boot

| Item | Detail |
|---|---|
| Roles | `SuperAdmin`, `BandCoordinator`, `NetController` |
| SuperAdmin user | Email/password from `SeedAdmin` config |
| 80m Early Net | Daily at 03:00z |
| 80m Late Net | Friday + Saturday at 05:00z |
| 80m Holiday Net | No auto-schedule rules — BC assigns sessions manually as needed |

---

## Further Documentation

- [Configuration Reference](docs/configuration.md) — appsettings, user secrets, production environment variables
- [Deployment](docs/deployment.md) — publish, systemd service, Apache reverse proxy, SSL
- [Roles](docs/roles.md) — what each role can do
- [Project Structure](docs/project-structure.md) — file layout and data model notes
- [Session Generation](docs/session-generation.md) — how the background scheduler works, EF migration commands

---

## License

MIT
