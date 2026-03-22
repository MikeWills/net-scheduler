using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

public class ScheduleController : Controller
{
    private readonly IScheduleService _scheduleService;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppSettings _appSettings;

    public ScheduleController(
        IScheduleService scheduleService,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<AppSettings> appSettings)
    {
        _scheduleService = scheduleService;
        _db = db;
        _userManager = userManager;
        _appSettings = appSettings.Value;
    }

    /// <summary>
    /// Builds an absolute URL for the iCal feed. Uses App:BaseUrl from configuration
    /// when set (production), otherwise falls back to the current request's host (development).
    /// </summary>
    private string? BuildIcalUrl(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var baseUrl = _appSettings.BaseUrl?.TrimEnd('/');
        if (!string.IsNullOrEmpty(baseUrl))
            return $"{baseUrl}/Ical/Feed/{token}";

        return Url.Action("Feed", "Ical", new { token }, Request.Scheme);
    }

    /// <summary>Public rolling 7-day schedule — no login required.</summary>
    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Start from yesterday (UTC) so US-evening sessions whose UTC date is "today"
        // but local date is "yesterday" (e.g. 03:00z Mon = 11 PM Sun ET) are included.
        // End at today+7 so the last local Saturday is fully covered: its early net at
        // 03:00z falls on the next UTC day (Sunday), one day beyond today+6.
        var from = today.AddDays(-1);
        var end = today.AddDays(7);

        var vm = await _scheduleService.GetPublicScheduleAsync(from, end);
        return View(vm);
    }

    /// <summary>Logged-in user's personal dashboard.</summary>
    [Authorize]
    public async Task<IActionResult> Dashboard()
    {
        var user = await _db.Users
            .Include(u => u.NetController)
            .FirstOrDefaultAsync(u => u.Id == _userManager.GetUserId(User));

        if (user?.NetController is null)
            return View(new DashboardViewModel());

        var nc = user.NetController;

        // Auto-generate an iCal token if one doesn't exist yet
        if (string.IsNullOrEmpty(nc.IcalToken))
        {
            nc.IcalToken = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = today.AddDays(63); // 9 weeks — covers a full 2-month lookahead

        // Net preferences for this controller
        var myPreferenceIds = (await _db.NetPreferences
            .Where(p => p.NetControllerId == nc.Id)
            .Select(p => p.NetId)
            .ToListAsync())
            .ToHashSet();

        // All active nets (for the preferences UI)
        var allNets = await _db.Nets
            .Where(n => n.IsActive)
            .OrderBy(n => n.Name)
            .ToListAsync();

        // My upcoming sessions (from standing assignments)
        var myStanding = await _db.StandingAssignments
            .Include(sa => sa.Net)
            .Where(sa =>
                sa.NetControllerId == nc.Id &&
                (sa.EffectiveTo == null || sa.EffectiveTo >= today))
            .ToListAsync();

        // Sessions I'm explicitly assigned to
        var myExplicit = await _db.SessionAssignments
            .Include(a => a.NetSession).ThenInclude(s => s.Net)
            .Include(a => a.NetSession).ThenInclude(s => s.Assignments.Where(b => b.Status != AssignmentStatus.Cancelled))
            .Where(a =>
                a.NetControllerId == nc.Id &&
                a.Status != AssignmentStatus.Cancelled &&
                a.NetSession.SessionDate >= today &&
                a.NetSession.SessionDate <= end)
            .OrderBy(a => a.NetSession.SessionDate)
            .ToListAsync();

        // My unavailabilities in the window — expand each date range to individual dates
        var myUnavailableRanges = await _db.Unavailabilities
            .Where(u => u.NetControllerId == nc.Id && u.EndDate >= today)
            .ToListAsync();
        var myUnavailable = new HashSet<DateOnly>();
        foreach (var u in myUnavailableRanges)
            for (var d = u.StartDate; d <= u.EndDate; d = d.AddDays(1))
                myUnavailable.Add(d);

        // Open slots across active nets — filter by preferences if set
        var openSessions = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
                .ThenInclude(a => a.NetController)
            .Where(s =>
                s.Net.IsActive &&
                s.SessionDate >= today &&
                s.SessionDate <= end)
            .OrderBy(s => s.SessionDate)
            .ToListAsync();

        // Load standing assignments to check which sessions are open
        var allStanding = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa => sa.EffectiveTo == null || sa.EffectiveTo >= today)
            .ToListAsync();

        var allUnavailabilities = await _db.Unavailabilities
            .Where(u => u.StartDate <= end && u.EndDate >= today)
            .ToListAsync();

        var openSlots = new List<OpenSlotItem>();
        var backupSessions = new List<BackupRequestItem>();

        foreach (var session in openSessions)
        {
            // Skip sessions whose net is outside its season window
            if (!session.Net.IsInSeasonForDate(session.SessionDate)) continue;

            var standing = allStanding.FirstOrDefault(sa =>
                sa.NetId == session.NetId &&
                sa.DayOfWeek == session.SessionDate.DayOfWeek &&
                sa.EffectiveFrom <= session.SessionDate &&
                (sa.EffectiveTo == null || sa.EffectiveTo >= session.SessionDate));

            bool hasExplicit = session.Assignments.Any(a => a.AssignmentType != AssignmentType.Volunteer || a.Status == AssignmentStatus.Confirmed);
            if (!hasExplicit)
            {
                bool isOpen = session.IsForcedOpen || standing is null ||
                    allUnavailabilities.Any(u =>
                        u.NetControllerId == standing.NetControllerId &&
                        u.StartDate <= session.SessionDate && u.EndDate >= session.SessionDate &&
                        (u.NetId == null || u.NetId == session.NetId));

                if (isOpen)
                {
                    // Apply net preference filter — skip if preferences set and this net not in them
                    if (myPreferenceIds.Count > 0 && !myPreferenceIds.Contains(session.NetId)) continue;

                    bool alreadyVolunteered = session.Assignments.Any(a =>
                        a.NetControllerId == nc.Id &&
                        a.AssignmentType == AssignmentType.Volunteer &&
                        a.Status != AssignmentStatus.Cancelled);

                    openSlots.Add(new OpenSlotItem
                    {
                        SessionId = session.Id,
                        NetName = session.Net.Name,
                        SessionDate = session.SessionDate,
                        ScheduledTimeUtc = session.ScheduledTimeUtc,
                        AlreadyVolunteered = alreadyVolunteered
                    });
                }
            }

            // Backup request: session has BackupRequested and I'm not the one who requested it
            if (session.BackupRequested)
            {
                var backups = session.Assignments
                    .Where(a => a.AssignmentType == AssignmentType.Backup && a.Status != AssignmentStatus.Cancelled)
                    .ToList();

                // Don't show if I'm already maxed (2 backups and neither is me, meaning no slot)
                bool alreadyStandingBy = backups.Any(a => a.NetControllerId == nc.Id);
                bool full = backups.Count >= 2 && !alreadyStandingBy;
                if (full) continue;

                // Determine who the requesting NCS is (standing or explicit non-backup assignment)
                var requestingNcsAssignment = session.Assignments
                    .FirstOrDefault(a => a.AssignmentType != AssignmentType.Backup && a.AssignmentType != AssignmentType.Volunteer && a.Status != AssignmentStatus.Cancelled);
                string requestingCallsign = requestingNcsAssignment?.NetController?.Callsign
                    ?? standing?.NetController?.Callsign
                    ?? "Unknown";

                // Don't show backup request to the requesting NCS themselves
                bool iAmTheNcs = requestingNcsAssignment?.NetControllerId == nc.Id
                    || (requestingNcsAssignment is null && standing?.NetControllerId == nc.Id);
                if (iAmTheNcs) continue;

                // Apply net preference filter for backup requests too
                if (myPreferenceIds.Count > 0 && !myPreferenceIds.Contains(session.NetId)) continue;

                backupSessions.Add(new BackupRequestItem
                {
                    SessionId = session.Id,
                    NetName = session.Net.Name,
                    SessionDate = session.SessionDate,
                    ScheduledTimeUtc = session.ScheduledTimeUtc,
                    RequestingNcsCallsign = requestingCallsign,
                    BackupCount = backups.Count,
                    AlreadyStandingBy = alreadyStandingBy
                });
            }
        }

        // ── My Upcoming Nets (next 14 days) ───────────────────────────────────
        var in14 = today.AddDays(13);

        // Load any sessions that already exist in the window for my standing nets,
        // so we can detect if I've been subbed out on a specific date.
        var standingNetIds = myStanding.Select(sa => sa.NetId).Distinct().ToList();
        var sessionsInWindow = await _db.NetSessions
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
                .ThenInclude(a => a.NetController)
            .Where(s => standingNetIds.Contains(s.NetId)
                     && s.SessionDate >= today
                     && s.SessionDate <= in14)
            .ToListAsync();

        var upcoming = new List<UpcomingNetItem>();

        // Standing assignments → generate actual dates in the window
        foreach (var sa in myStanding)
        {
            for (var d = today; d <= in14; d = d.AddDays(1))
            {
                if (d.DayOfWeek != sa.DayOfWeek) continue;
                if (myUnavailable.Contains(d)) continue;

                // Skip if a session exists and has a confirmed substitute assigned to someone else
                var session = sessionsInWindow
                    .FirstOrDefault(s => s.NetId == sa.NetId && s.SessionDate == d);
                bool subbedOut = session?.Assignments.Any(a =>
                    a.AssignmentType == AssignmentType.Substitute &&
                    a.NetControllerId != nc.Id) ?? false;
                if (subbedOut) continue;

                var backups = session?.Assignments
                    .Where(a => a.AssignmentType == AssignmentType.Backup && a.Status != AssignmentStatus.Cancelled)
                    .ToList() ?? [];

                upcoming.Add(new UpcomingNetItem
                {
                    SessionId = session?.Id,
                    SessionDate = d,
                    ScheduledTimeUtc = sa.Net!.ScheduledTimeUtc,
                    NetName = sa.Net.Name,
                    FrequencyMhz = sa.Net.FrequencyMhz,
                    FrequencyRange = sa.Net.FrequencyRange,
                    IsSubstitute = false,
                    BackupRequested = session?.BackupRequested ?? false,
                    BackupCount = backups.Count,
                    AlreadyStandingBy = backups.Any(a => a.NetControllerId == nc.Id)
                });
            }
        }

        // Explicit confirmed assignments where I'm the sub (don't double-count standing dates)
        foreach (var a in myExplicit.Where(a =>
            a.NetSession.SessionDate <= in14 &&
            a.AssignmentType == AssignmentType.Substitute))
        {
            var sessionDate = a.NetSession.SessionDate;
            var netName = a.NetSession.Net!.Name;
            if (!upcoming.Any(u => u.SessionDate == sessionDate && u.NetName == netName))
            {
                var backups = a.NetSession.Assignments
                    .Where(b => b.AssignmentType == AssignmentType.Backup && b.Status != AssignmentStatus.Cancelled)
                    .ToList();

                upcoming.Add(new UpcomingNetItem
                {
                    SessionId = a.NetSessionId,
                    SessionDate = sessionDate,
                    ScheduledTimeUtc = a.NetSession.ScheduledTimeUtc,
                    NetName = netName,
                    FrequencyMhz = a.NetSession.Net.FrequencyMhz,
                    FrequencyRange = a.NetSession.Net.FrequencyRange,
                    IsSubstitute = true,
                    BackupRequested = a.NetSession.BackupRequested,
                    BackupCount = backups.Count,
                    AlreadyStandingBy = backups.Any(b => b.NetControllerId == nc.Id)
                });
            }
        }

        var icalUrl = BuildIcalUrl(nc.IcalToken);

        var vm = new DashboardViewModel
        {
            Callsign = nc.Callsign,
            MemberNumber = nc.MemberNumber,
            MyExplicitAssignments = myExplicit,
            MyStandingAssignments = myStanding,
            MyUnavailableDates = myUnavailable,
            OpenSlots = openSlots.OrderBy(s => s.SessionDate).ToList(),
            MyUpcomingNets = upcoming.OrderBy(u => u.SessionDate).ThenBy(u => u.ScheduledTimeUtc).ToList(),
            BackupSessions = backupSessions.OrderBy(b => b.SessionDate).ToList(),
            IcalFeedUrl = icalUrl,
            NetPreferenceIds = myPreferenceIds,
            AllNets = allNets
        };

        return View(vm);
    }

    /// <summary>Save this controller's net preferences (which nets they are willing to run).</summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreferences(int[] selectedNetIds)
    {
        var userId = _userManager.GetUserId(User)!;
        var nc = await _db.NetControllers.FirstOrDefaultAsync(c => c.UserId == userId);
        if (nc is null) return Forbid();

        // Replace all existing preferences
        var existing = await _db.NetPreferences.Where(p => p.NetControllerId == nc.Id).ToListAsync();
        _db.NetPreferences.RemoveRange(existing);

        foreach (var netId in selectedNetIds.Distinct())
        {
            _db.NetPreferences.Add(new NetControllerNetPreference
            {
                NetControllerId = nc.Id,
                NetId = netId
            });
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Net preferences saved.";
        return RedirectToAction("Dashboard");
    }
}
