# Project Structure

```
NcsScheduler/
├── Controllers/
│   ├── AccountController.cs          # Login, logout, profile, forgot/reset password
│   ├── AdminController.cs            # Users, roles, standing assignments
│   ├── AssignmentsController.cs      # Coordinator assignment management + calendar
│   ├── ControllersController.cs      # Net controller CRUD + activate/deactivate
│   ├── CoordinatorsController.cs     # Band coordinator management
│   ├── IcalController.cs             # NCS personal feed (/Ical/Feed/{token}) + BC feed (/Ical/BcFeed/{token})
│   ├── InvitationController.cs       # Send invite (inline) + accept invite flow
│   ├── NetsController.cs             # Net CRUD
│   ├── ScheduleController.cs         # Public schedule + personal dashboard
│   ├── UnavailabilityController.cs   # Self-service unavailability
│   └── VolunteerController.cs        # Volunteer sign-up, backup requests, net preferences save
├── Data/
│   ├── ApplicationDbContext.cs       # EF DbContext + model configuration
│   ├── DbSeeder.cs                   # Roles, admin user, default nets
│   └── Migrations/                   # EF Core migration history
├── Models/
│   ├── Domain/                       # EF entities
│   │   ├── Net.cs, NetSession.cs, NetScheduleRule.cs
│   │   ├── NetController.cs, StandingAssignment.cs, SessionAssignment.cs
│   │   ├── Unavailability.cs, Invitation.cs, BandCoordinator.cs
│   │   ├── NetControllerNetPreference.cs  # Per-user net opt-in preferences
│   │   └── Enums.cs                  # AssignmentType (Regular/Substitute/Volunteer/Backup), AssignmentStatus
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

## Key Data Model Notes

- `Net.ScheduledTimeUtc` and `NetSession.ScheduledTimeUtc` — stored in UTC
- `NetSession.SessionDate` — the UTC calendar date of the session
- `NetScheduleRule.DayOfWeek` — UTC day of week (used for session generation)
- `StandingAssignment.DayOfWeek` — **Eastern local** day of week (requires conversion when matching against UTC dates)
- `ScheduleService` has `ToEasternDate()`/`ToEasternTime()` helpers for UTC↔Eastern conversion
