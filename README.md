# NCS Scheduler

A self-hosted web application for managing amateur radio net controllers. Replaces the club's Google Spreadsheet with self-service unavailability reporting, volunteer sign-up, coordinator assignment tools, and a public schedule view.

Built for OMISS 80m nets but designed to be configurable for any club running multiple nets across different days and times.

---

## Features

### Public
- Rolling schedule view showing the current week's sessions across all active nets
- Callsign and member number displayed for each assigned NCS
- Open slots highlighted in red; volunteer-pending slots in yellow
- Local time display alongside UTC

### Net Controllers (logged-in)
- Personal dashboard showing upcoming sessions and open slots in your nets
- iCal calendar feed — subscribe in any calendar app; token auto-generated on first dashboard visit
- Self-service unavailability reporting (date ranges, per-net or all nets)
- One-click volunteer sign-up for open slots
- Password reset via email ("Forgot your password?" link on login page)

### Band Coordinators
- Manage assignments: assign subs, confirm volunteers, or manually open any date
- "Assign Sub for Any Date" — create a session on any date even outside normal schedule rules
- Weekly calendar view (Sunday–Saturday) formatted for copy-paste, with change highlighting vs. the prior week
- Read-only view of the Net Controllers list
- View limited to nets under the coordinator's management

### Super Admins
- Full user, role, and net management
- Net controller list — add, edit, activate/deactivate controllers
- Send account invitations directly from the Net Controllers list (uses email on file; tokenized 7-day link)
- Standing assignments — set the default NCS per net per day of week with effective date ranges
- Band Coordinator promotion/demotion

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

After the app starts for the first time:

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

## Roles

| Role | What they can do |
|---|---|
| `SuperAdmin` | Everything — users, roles, nets, standing assignments, add/edit/activate controllers, send invites |
| `BandCoordinator` | Assignments, weekly calendar, read-only Net Controllers list |
| `NetController` | Dashboard, unavailability, volunteer sign-up |
| *(anonymous)* | Public schedule only |

Roles are managed at **Admin → Manage Users**. A user can hold multiple roles. Changes take effect on next login.

---

## Configuration Reference

### `appsettings.json` — committed, no secrets

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

### `appsettings.Development.json` — local only, not committed to source control

```json
{
  "SeedAdmin": {
    "Email": "admin@example.com",
    "Password": "ChangeMe123!"
  }
}
```

### User Secrets — local development credential store

```bash
dotnet user-secrets set "Email:Username" "smtp-username"  --project NcsScheduler/NcsScheduler.csproj
dotnet user-secrets set "Email:Password" "smtp-password"  --project NcsScheduler/NcsScheduler.csproj
dotnet user-secrets list                                   --project NcsScheduler/NcsScheduler.csproj
```

Secrets are stored in `%APPDATA%\Microsoft\UserSecrets\` (Windows) or `~/.microsoft/usersecrets/` (Linux/macOS) — never inside the repo.

### Production — environment variables

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

---

## Project Structure

```
NcsScheduler/
├── Controllers/
│   ├── AccountController.cs          # Login, logout, profile, forgot/reset password
│   ├── AdminController.cs            # Users, roles, standing assignments
│   ├── AssignmentsController.cs      # Coordinator assignment management + calendar
│   ├── ControllersController.cs      # Net controller CRUD + activate/deactivate
│   ├── CoordinatorsController.cs     # Band coordinator management
│   ├── IcalController.cs             # Personal iCal calendar feed (token-authenticated)
│   ├── InvitationController.cs       # Send invite (inline) + accept invite flow
│   ├── NetsController.cs             # Net CRUD
│   ├── ScheduleController.cs         # Public schedule + personal dashboard
│   ├── UnavailabilityController.cs   # Self-service unavailability
│   └── VolunteerController.cs        # Volunteer sign-up for open slots
├── Data/
│   ├── ApplicationDbContext.cs       # EF DbContext + model configuration
│   ├── DbSeeder.cs                   # Roles, admin user, default nets
│   └── Migrations/                   # EF Core migration history
├── Models/
│   ├── Domain/                       # EF entities
│   │   ├── Net.cs, NetSession.cs, NetScheduleRule.cs
│   │   ├── NetController.cs, StandingAssignment.cs, SessionAssignment.cs
│   │   ├── Unavailability.cs, Invitation.cs, BandCoordinator.cs
│   │   └── Enums.cs                  # AssignmentType, AssignmentStatus
│   └── ViewModels/                   # View-specific models
├── Services/
│   ├── IScheduleService.cs / ScheduleService.cs   # Session generation & slot resolution
│   ├── IEmailService.cs / EmailService.cs         # MailKit SMTP
│   ├── EmailSettings.cs                           # Typed config binding
│   └── SessionGeneratorService.cs                 # IHostedService background worker
├── Views/
│   ├── Schedule/      # Public schedule (Index) + personal dashboard (Dashboard)
│   ├── Assignments/   # Coordinator tools (Index, Calendar)
│   ├── Unavailability/
│   ├── Volunteer/
│   ├── Admin/         # Users, standing assignments
│   └── Shared/        # Layout, partials
├── wwwroot/           # Static assets (Bootstrap 5, jQuery, site CSS/JS)
├── appsettings.json
├── appsettings.Development.json
└── Program.cs
```

---

## Common Commands

```bash
# Run
dotnet run --project NcsScheduler/NcsScheduler.csproj

# Watch (auto-reload)
dotnet watch --project NcsScheduler/NcsScheduler.csproj

# Build
dotnet build

# Add an EF migration
dotnet ef migrations add MigrationName --project NcsScheduler/NcsScheduler.csproj

# Apply migrations manually (the app also applies them automatically on startup)
dotnet ef database update --project NcsScheduler/NcsScheduler.csproj

# List stored User Secrets
dotnet user-secrets list --project NcsScheduler/NcsScheduler.csproj
```

---

## How Session Generation Works

A background service (`SessionGeneratorService`) runs immediately on startup and again every 24 hours. It calls `ScheduleService.GenerateAllSessionsAsync()` which:

1. Loads each active net and its `NetScheduleRule` entries (which days of the week it runs)
2. Iterates over the next 9 weeks, creating a `NetSession` record for each matching day
3. Respects the net's season window if configured (e.g., a net that only runs May–September)
4. Skips dates that already have a session — the generator is safe to run repeatedly

The public schedule and coordinator calendar always read from these pre-generated sessions.

---

## License

MIT
