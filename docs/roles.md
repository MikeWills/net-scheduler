# Roles

| Role | What they can do |
|---|---|
| `SuperAdmin` | Everything — users, roles, nets, standing assignments, add/edit/activate controllers, send invites |
| `BandCoordinator` | Assignments, weekly calendar, read-only Net Controllers list |
| `NetController` | Dashboard, unavailability, volunteer sign-up |
| *(anonymous)* | Public schedule only |

Roles are managed at **Admin → Manage Users**. A user can hold multiple roles. Changes take effect on next login.

## Feature breakdown by role

### SuperAdmin
- Full user, role, and net management
- Net controller list — add, edit, activate/deactivate controllers
- Send account invitations directly from the Net Controllers list (uses email on file; tokenized 7-day link)
- Standing assignments — set the default NCS per net per day of week with effective date ranges
- Band Coordinator promotion/demotion

### Band Coordinator
- Manage assignments: assign subs, confirm volunteers, or manually open any date
- "Assign Sub for Any Date" — create a session on any date even outside normal schedule rules
- Weekly calendar view (Sunday–Saturday) formatted for copy-paste, with change highlighting vs. the prior week
- Hover over any NCS callsign to see their regular standing nets and the date they last ran
- Backup standby status visible per session cell (⚠ Backup badge with standby callsigns)
- iCal calendar feed showing all managed-net sessions with the assigned NCS
- Read-only view of the Net Controllers list
- View limited to nets under the coordinator's management

### Net Controller
- Personal dashboard showing upcoming sessions and open slots in your nets
- iCal calendar feed — subscribe in any calendar app; token auto-generated on first dashboard visit
- Self-service unavailability reporting (date ranges, per-net or all nets)
- One-click volunteer sign-up for open slots
- Net preferences — select which nets you are willing to cover
- Backup requests — flag a session as needing a backup; opted-in NCS members receive an email and can stand by (capped at 2)
- Password reset via email
