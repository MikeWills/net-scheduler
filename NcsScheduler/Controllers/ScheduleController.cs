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
        // Start from yesterday (UTC) so US-evening sessions whose UTC date is
        var from = today.AddDays(-1);
        var end = today.AddDays(6);

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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = today.AddDays(63); // 9 weeks — covers a full 2-month lookahead

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

        // Open slots in nets I'm in the pool for
        var myNetIds = await _db.NetControllerPool
            .Where(p => p.NetControllerId == nc.Id && p.IsActive)
            .Select(p => p.NetId)
            .ToListAsync();

        // Also include standing assignment nets
        myNetIds.AddRange(myStanding.Select(sa => sa.NetId));
        myNetIds = myNetIds.Distinct().ToList();

        var openSessions = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
            .Where(s =>
                myNetIds.Contains(s.NetId) &&
                s.Net.IsActive &&
                s.SessionDate >= today &&
                s.SessionDate <= end)
            .OrderBy(s => s.SessionDate)
            .ToListAsync();

        // Load standing assignments to check which sessions are open
        var allStanding = await _db.StandingAssignments
            .Where(sa => sa.EffectiveTo == null || sa.EffectiveTo >= today)
            .ToListAsync();

        var allUnavailabilities = await _db.Unavailabilities
            .Where(u => u.StartDate <= end && u.EndDate >= today)
            .ToListAsync();

        var openSlots = new List<OpenSlotItem>();
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
            if (hasExplicit) continue;

            bool isOpen = session.IsForcedOpen || standing is null ||
                allUnavailabilities.Any(u =>
                    u.NetControllerId == standing.NetControllerId &&
                    u.StartDate <= session.SessionDate && u.EndDate >= session.SessionDate &&
                    (u.NetId == null || u.NetId == session.NetId));

            if (!isOpen) continue;

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

        // ── My Upcoming Nets (next 14 days) ───────────────────────────────────
        var in14 = today.AddDays(13);

        // Load any sessions that already exist in the window for my standing nets,
        // so we can detect if I've been subbed out on a specific date.
        var standingNetIds = myStanding.Select(sa => sa.NetId).Distinct().ToList();
        var sessionsInWindow = await _db.NetSessions
            .Include(s => s.Assignments.Where(a => a.Status == AssignmentStatus.Confirmed))
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

                upcoming.Add(new UpcomingNetItem
                {
                    SessionDate = d,
                    ScheduledTimeUtc = sa.Net!.ScheduledTimeUtc,
                    NetName = sa.Net.Name,
                    FrequencyMhz = sa.Net.FrequencyMhz,
                    FrequencyRange = sa.Net.FrequencyRange,
                    IsSubstitute = false
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
                upcoming.Add(new UpcomingNetItem
                {
                    SessionDate = sessionDate,
                    ScheduledTimeUtc = a.NetSession.ScheduledTimeUtc,
                    NetName = netName,
                    FrequencyMhz = a.NetSession.Net.FrequencyMhz,
                    FrequencyRange = a.NetSession.Net.FrequencyRange,
                    IsSubstitute = true
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
            IcalFeedUrl = icalUrl
        };

        return View(vm);
    }
}
