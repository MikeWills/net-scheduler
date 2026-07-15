using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NcsScheduler.Data;
using NcsScheduler.Helpers;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

[Authorize(Policy = "CanManageNets")]
public class AssignmentsController : Controller
{

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly AppSettings _appSettings;

    public AssignmentsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        IEmailService emailService, IOptions<AppSettings> appSettings)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
        _appSettings = appSettings.Value;
    }

    private string? BuildBcIcalUrl(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var baseUrl = _appSettings.BaseUrl?.TrimEnd('/');
        if (!string.IsNullOrEmpty(baseUrl))
            return $"{baseUrl}/Ical/BcFeed/{token}";
        return Url.Action("BcFeed", "Ical", new { token }, Request.Scheme);
    }

    // Exceptions are paged 10 at a time; the lookahead window matches the 9-week
    // horizon that SessionGeneratorService actually generates sessions for, since
    // there's nothing to page through beyond that.
    private const int ExceptionsPageSize = 10;
    private const int ExceptionsWindowWeeks = 9;

    // GET: Assignments/Index
    // Section 1 — Regular Schedule grid (standing assignments, repeat weekly)
    // Section 2 — Upcoming exceptions (open slots / volunteers needing a sub)
    public async Task<IActionResult> Index(int exceptionsPage = 0)
    {
        if (exceptionsPage < 0) exceptionsPage = 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var exceptionWindow = today.AddDays(ExceptionsWindowWeeks * 7);

        var managedNetIds = await GetManagedNetIdsAsync();

        // ── Regular Schedule ──────────────────────────────────────────────────
        var standings = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa => sa.EffectiveTo == null && managedNetIds.Contains(sa.NetId))
            .ToListAsync();

        var rules = await _db.NetScheduleRules
            .Where(r => r.IsActive && managedNetIds.Contains(r.NetId))
            .ToListAsync();

        var nets = (await _db.Nets
            .Where(n => n.IsActive && managedNetIds.Contains(n.Id))
            .ToListAsync())
            .OrderBy(n => BandHelper.SortKey(n.Band))
            .ThenBy(n => n.ScheduledTimeUtc)
            .ThenBy(n => n.Name)
            .ToList();

        var controllers = await _db.NetControllers
            .Where(nc => nc.IsActive)
            .OrderBy(nc => nc.Callsign)
            .ToListAsync();

        // ── Exceptions (next 4 weeks) ─────────────────────────────────────────
        var unavailabilities = await _db.Unavailabilities
            .Where(u => u.StartDate <= exceptionWindow && u.EndDate >= today)
            .ToListAsync();

        var sessions = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
                .ThenInclude(a => a.NetController)
            .Where(s => managedNetIds.Contains(s.NetId)
                     && s.SessionDate >= today
                     && s.SessionDate <= exceptionWindow)
            .OrderBy(s => s.SessionDate).ThenBy(s => s.ScheduledTimeUtc)
            .ToListAsync();

        // Standing map for quick exception detection: (netId, dayOfWeek) → assignment
        var standingMap = standings
            .GroupBy(sa => (sa.NetId, sa.DayOfWeek))
            .ToDictionary(g => g.Key, g => g.First());

        var exceptions = new List<AssignmentSlotItem>();
        foreach (var session in sessions)
        {
            // Already has a confirmed explicit assignment → covered, skip
            var confirmed = session.Assignments
                .FirstOrDefault(a => a.AssignmentType != AssignmentType.Volunteer
                                  && a.Status == AssignmentStatus.Confirmed);
            if (confirmed is not null) continue;

            var easternDate = DateConverter.ToEasternDate(session.SessionDate, session.ScheduledTimeUtc);
            standingMap.TryGetValue((session.NetId, easternDate.DayOfWeek), out var standing);

            // Unavailability dates are in Eastern; compare against the Eastern date
            bool regularUnavailable = standing is not null && unavailabilities.Any(u =>
                u.NetControllerId == standing.NetControllerId &&
                u.StartDate <= easternDate && u.EndDate >= easternDate &&
                (u.NetId == null || u.NetId == session.NetId));

            bool isOpen = standing is null || regularUnavailable || session.IsForcedOpen;
            bool hasVolunteer = session.Assignments.Any(a => a.AssignmentType == AssignmentType.Volunteer);

            if (!isOpen && !hasVolunteer) continue;

            exceptions.Add(new AssignmentSlotItem
            {
                Session = session,
                StandingController = standing?.NetController,
                Volunteers = session.Assignments
                    .Where(a => a.AssignmentType == AssignmentType.Volunteer)
                    .ToList()
            });
        }

        // Sessions that have been manually force-opened (future dates only)
        var forcedOpen = await _db.NetSessions
            .Include(s => s.Net)
            .Where(s => s.IsForcedOpen
                     && managedNetIds.Contains(s.NetId)
                     && s.SessionDate >= today)
            .OrderBy(s => s.SessionDate)
            .ToListAsync();

        // Clamp to the last valid page once we know how many exceptions there are,
        // then page the already-filtered in-memory list (filtering can't be pushed
        // down to SQL — it depends on unavailability/standing lookups above).
        var totalPages = (int)Math.Ceiling(exceptions.Count / (double)ExceptionsPageSize);
        if (exceptionsPage > 0 && exceptionsPage >= totalPages) exceptionsPage = Math.Max(0, totalPages - 1);

        var pagedExceptions = exceptions
            .Skip(exceptionsPage * ExceptionsPageSize)
            .Take(ExceptionsPageSize)
            .ToList();

        ViewBag.Nets = nets;
        ViewBag.Controllers = controllers;
        ViewBag.Rules = rules;
        ViewBag.Standings = standings;
        ViewBag.Exceptions = pagedExceptions;
        ViewBag.ExceptionsPage = exceptionsPage;
        ViewBag.ExceptionsPageSize = ExceptionsPageSize;
        ViewBag.ExceptionsTotalCount = exceptions.Count;
        ViewBag.ExceptionsHasNextPage = (exceptionsPage + 1) * ExceptionsPageSize < exceptions.Count;
        ViewBag.ExceptionsWindowWeeks = ExceptionsWindowWeeks;
        ViewBag.ForcedOpen = forcedOpen;
        return View();
    }

    // POST: SetStanding — creates/updates a recurring weekly assignment for a net+day
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStanding(int netId, int netControllerId, DayOfWeek dayOfWeek)
    {
        var managedNetIds = await GetManagedNetIdsAsync();
        if (!managedNetIds.Contains(netId)) return Forbid();

        // Close out any existing standing assignment for this net+day
        var existing = await _db.StandingAssignments
            .Where(sa => sa.NetId == netId && sa.DayOfWeek == dayOfWeek && sa.EffectiveTo == null)
            .ToListAsync();
        foreach (var sa in existing)
            sa.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        if (netControllerId > 0)  // 0 = "— Unassigned —", just clears the slot
        {
            _db.StandingAssignments.Add(new StandingAssignment
            {
                NetId = netId,
                NetControllerId = netControllerId,
                DayOfWeek = dayOfWeek,
                EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow)
            });
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Regular assignment saved — will repeat each week automatically.";
        return RedirectToAction("Index");
    }

    // POST: Assign — one-time sub for a specific session date only
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int sessionId, int controllerId, int exceptionsPage = 0)
    {
        var managedNetIds = await GetManagedNetIdsAsync();
        var session = await _db.NetSessions.Include(s => s.Net).FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null || !managedNetIds.Contains(session.NetId)) return Forbid();

        var controller = await _db.NetControllers.FindAsync(controllerId);
        if (controller is null) return NotFound();

        // Cancel all previous assignments for this session before adding the new one
        var prior = await _db.SessionAssignments
            .Where(a => a.NetSessionId == sessionId)
            .ToListAsync();
        foreach (var a in prior) a.Status = AssignmentStatus.Cancelled;

        _db.SessionAssignments.Add(new SessionAssignment
        {
            NetSessionId = sessionId,
            NetControllerId = controllerId,
            AssignmentType = AssignmentType.Substitute,
            Status = AssignmentStatus.Confirmed,
            AssignedByUserId = _userManager.GetUserId(User)
        });

        // Slot is filled now -- clear any manual "force open" flag so it stops
        // lingering in the force-marked-open list.
        if (session.IsForcedOpen)
            session.IsForcedOpen = false;

        await _db.SaveChangesAsync();

        if (controller.NotifyOnAssigned)
            await _emailService.SendAssignmentConfirmationAsync(controller, session);

        var displayDate = DateConverter.ToEasternDate(session.SessionDate, session.ScheduledTimeUtc);
        TempData["Success"] = $"Assigned {controller.Callsign} to {session.Net?.Name} on {displayDate:MMMM d, yyyy}.";
        return RedirectToAction("Index", new { exceptionsPage });
    }

    // POST: Confirm — promote a volunteer to confirmed for a specific session
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int assignmentId, int exceptionsPage = 0)
    {
        var assignment = await _db.SessionAssignments
            .Include(a => a.NetController)
            .Include(a => a.NetSession).ThenInclude(s => s.Net)
            .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment is null) return NotFound();

        var managedNetIds = await GetManagedNetIdsAsync();
        if (!managedNetIds.Contains(assignment.NetSession.NetId)) return Forbid();

        assignment.Status = AssignmentStatus.Confirmed;

        // Cancel every other non-confirmed assignment for this session so the
        // slot resolves cleanly and other volunteers know they're not needed.
        var others = await _db.SessionAssignments
            .Where(a => a.NetSessionId == assignment.NetSessionId
                     && a.Id != assignment.Id
                     && a.Status != AssignmentStatus.Confirmed)
            .ToListAsync();
        foreach (var other in others)
            other.Status = AssignmentStatus.Cancelled;

        // If this session was force-opened, clear that flag now that it's covered.
        if (assignment.NetSession.IsForcedOpen)
            assignment.NetSession.IsForcedOpen = false;

        await _db.SaveChangesAsync();

        if (assignment.NetController.NotifyOnAssigned)
            await _emailService.SendAssignmentConfirmationAsync(assignment.NetController, assignment.NetSession);

        TempData["Success"] = $"Confirmed {assignment.NetController.Callsign}.";
        return RedirectToAction("Index", new { exceptionsPage });
    }

    // POST: AssignByDate — one-time sub for any date, creates the session if it doesn't exist yet
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignByDate(int netId, string date, int controllerId)
    {
        var managedNetIds = await GetManagedNetIdsAsync();
        if (!managedNetIds.Contains(netId)) return Forbid();

        if (!DateOnly.TryParse(date, out var sessionDate))
        {
            TempData["Error"] = "Invalid date.";
            return RedirectToAction("Index");
        }

        if (controllerId <= 0)
        {
            TempData["Error"] = "Please select a controller.";
            return RedirectToAction("Index");
        }

        var net = await _db.Nets.FindAsync(netId);
        if (net is null) return NotFound();

        var controller = await _db.NetControllers.FindAsync(controllerId);
        if (controller is null) return NotFound();

        // The BC enters an Eastern date; convert to the UTC SessionDate used in the database
        var utcSessionDate = DateConverter.ToUtcSessionDate(sessionDate, net.ScheduledTimeUtc);

        // Find or create the session for this net+date
        var session = await _db.NetSessions
            .FirstOrDefaultAsync(s => s.NetId == netId && s.SessionDate == utcSessionDate);

        if (session is null)
        {
            session = new NetSession
            {
                NetId = netId,
                SessionDate = utcSessionDate,
                ScheduledTimeUtc = net.ScheduledTimeUtc
            };
            _db.NetSessions.Add(session);
            await _db.SaveChangesAsync(); // flush to get the new session Id
        }

        // Cancel all prior assignments for this session
        var prior = await _db.SessionAssignments
            .Where(a => a.NetSessionId == session.Id)
            .ToListAsync();
        foreach (var a in prior) a.Status = AssignmentStatus.Cancelled;

        _db.SessionAssignments.Add(new SessionAssignment
        {
            NetSessionId = session.Id,
            NetControllerId = controllerId,
            AssignmentType = AssignmentType.Substitute,
            Status = AssignmentStatus.Confirmed,
            AssignedByUserId = _userManager.GetUserId(User)
        });

        // Slot is filled now -- clear any manual "force open" flag so it stops
        // lingering in the force-marked-open list.
        if (session.IsForcedOpen)
            session.IsForcedOpen = false;

        await _db.SaveChangesAsync();

        // Attach net for email (already in memory)
        session.Net = net;
        if (controller.NotifyOnAssigned)
            await _emailService.SendAssignmentConfirmationAsync(controller, session);

        TempData["Success"] = $"Assigned {controller.Callsign} to {net.Name} on {sessionDate:MMMM d, yyyy}.";
        return RedirectToAction("Index");
    }

    // POST: ForceOpen — manually mark a net session as needing a sub, regardless of standing assignments
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForceOpen(int netId, string date)
    {
        var managedNetIds = await GetManagedNetIdsAsync();
        if (!managedNetIds.Contains(netId)) return Forbid();

        if (!DateOnly.TryParse(date, out var sessionDate))
        {
            TempData["Error"] = "Invalid date.";
            return RedirectToAction("Index");
        }

        var net = await _db.Nets.FindAsync(netId);
        if (net is null) return NotFound();

        // The BC enters an Eastern date; convert to the UTC SessionDate used in the database
        var utcSessionDate = DateConverter.ToUtcSessionDate(sessionDate, net.ScheduledTimeUtc);

        // Find or create the session
        var session = await _db.NetSessions
            .FirstOrDefaultAsync(s => s.NetId == netId && s.SessionDate == utcSessionDate);

        if (session is null)
        {
            session = new NetSession
            {
                NetId = netId,
                SessionDate = utcSessionDate,
                ScheduledTimeUtc = net.ScheduledTimeUtc,
                IsForcedOpen = true
            };
            _db.NetSessions.Add(session);
        }
        else
        {
            session.IsForcedOpen = true;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"{net.Name} on {sessionDate:MMMM d, yyyy} has been marked as open — it will show as NCS Needed.";
        return RedirectToAction("Index");
    }

    // POST: RemoveForceOpen — clear the forced-open flag from a session
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveForceOpen(int sessionId)
    {
        var session = await _db.NetSessions
            .Include(s => s.Net)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return NotFound();

        var managedNetIds = await GetManagedNetIdsAsync();
        if (!managedNetIds.Contains(session.NetId)) return Forbid();

        session.IsForcedOpen = false;
        await _db.SaveChangesAsync();

        var localDate = DateConverter.ToEasternDate(session.SessionDate, session.ScheduledTimeUtc);
        TempData["Success"] = $"Open status cleared for {session.Net?.Name} on {localDate:MMMM d, yyyy}.";
        return RedirectToAction("Index");
    }

    // GET: Assignments/Calendar — weekly grid for Sun–Sat
    // offset: number of weeks from the current week (0 = this week, 1 = next, -1 = last)
    public async Task<IActionResult> Calendar(int offset = 0)
    {
        // Use Eastern "today" so the week boundary is correct for US-evening nets
        // (e.g. at 03:00z Monday UTC it's still Sunday Eastern)
        var easternToday = DateConverter.TodayEastern();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Auto-generate an iCal token for this BC if they don't have one
        var userId = _userManager.GetUserId(User)!;
        var nc = await _db.NetControllers.FirstOrDefaultAsync(c => c.UserId == userId);
        if (nc is not null && string.IsNullOrEmpty(nc.IcalToken))
        {
            nc.IcalToken = Guid.NewGuid().ToString("N");
            await _db.SaveChangesAsync();
        }
        ViewBag.BcIcalFeedUrl = BuildBcIcalUrl(nc?.IcalToken);

        // Current week = the Sun–Sat block containing Eastern today, shifted by offset weeks.
        // Column dates represent Eastern calendar dates so overnight nets (03:00z Mon = Sun evening ET)
        // appear under the correct Eastern day.
        int daysSinceSunday = (int)easternToday.DayOfWeek; // Sunday=0 … Saturday=6
        var thisWeekSunday  = easternToday.AddDays(-daysSinceSunday);
        var weekStart       = thisWeekSunday.AddDays(offset * 7);
        var weekEnd         = weekStart.AddDays(6);
        var prevWeekStart   = weekStart.AddDays(-7);  // previous week (for comparison)
        ViewBag.WeekOffset = offset;

        var managedNetIds = await GetManagedNetIdsAsync();

        var nets = (await _db.Nets
            .Include(n => n.ScheduleRules.Where(r => r.IsActive))
            .Where(n => n.IsActive && managedNetIds.Contains(n.Id))
            .ToListAsync())
            .OrderBy(n => BandHelper.SortKey(n.Band))
            .ThenBy(n => n.ScheduledTimeUtc)
            .ThenBy(n => n.Name)
            .ToList();

        // Sessions for both weeks in one query.
        // Column dates are Eastern; UTC session dates can be up to +1 day ahead,
        // so extend the query window to capture overnight sessions.
        var allSessions = await _db.NetSessions
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
                .ThenInclude(a => a.NetController)
            .Where(s => managedNetIds.Contains(s.NetId)
                     && s.SessionDate >= prevWeekStart
                     && s.SessionDate <= weekEnd.AddDays(1))
            .ToListAsync();

        // Standing assignments that cover either week
        var standings = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa => managedNetIds.Contains(sa.NetId)
                      && (sa.EffectiveTo == null || sa.EffectiveTo >= prevWeekStart))
            .ToListAsync();

        // Unavailabilities that overlap either week
        var unavailabilities = await _db.Unavailabilities
            .Where(u => u.StartDate <= weekEnd && u.EndDate >= prevWeekStart)
            .ToListAsync();

        // All standing assignments (all managed nets, for hover tooltip standing net list)
        var allStandings = standings; // already covers managed nets and the window

        // Recent past sessions (last 90 days) for managed nets — used for "last ran" tooltip
        var historyStart = today.AddDays(-90);
        var recentSessions = await _db.NetSessions
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
            .Where(s => managedNetIds.Contains(s.NetId)
                     && s.SessionDate >= historyStart
                     && s.SessionDate < today)
            .ToListAsync();

        // All active standings across managed nets (for tooltip: "regularly scheduled for...")
        var allActiveStandings = await _db.StandingAssignments
            .Include(sa => sa.Net)
            .Where(sa => managedNetIds.Contains(sa.NetId) && sa.EffectiveTo == null)
            .ToListAsync();

        // Build per-controller lookup: last scheduled date and standing net names
        var lastScheduledByController = new Dictionary<int, DateOnly>();
        var standingNetsByController  = new Dictionary<int, List<string>>();

        foreach (var session in recentSessions)
        {
            // Use standing assignment to find the NCS for each past session
            var easternDay = DateConverter.ToEasternDate(session.SessionDate, session.ScheduledTimeUtc).DayOfWeek;
            var sa = allStandings.FirstOrDefault(x =>
                x.NetId == session.NetId &&
                x.DayOfWeek == easternDay &&
                x.EffectiveFrom <= session.SessionDate &&
                (x.EffectiveTo == null || x.EffectiveTo >= session.SessionDate));

            // Also check explicit confirmed assignments
            var explicitNcsId = session.Assignments
                .Where(a => a.AssignmentType != AssignmentType.Volunteer && a.AssignmentType != AssignmentType.Backup)
                .Select(a => (int?)a.NetControllerId)
                .FirstOrDefault();

            var ncsId = explicitNcsId ?? sa?.NetControllerId;
            if (ncsId is null) continue;

            if (!lastScheduledByController.TryGetValue(ncsId.Value, out var existing) || session.SessionDate > existing)
                lastScheduledByController[ncsId.Value] = session.SessionDate;
        }

        foreach (var sa in allActiveStandings)
        {
            if (!standingNetsByController.TryGetValue(sa.NetControllerId, out var list))
            {
                list = [];
                standingNetsByController[sa.NetControllerId] = list;
            }
            var label = $"{sa.Net.Name} ({sa.DayOfWeek.ToString()[..3]})";
            if (!list.Contains(label)) list.Add(label);
        }

        var vm = new BcCalendarViewModel { WeekStart = weekStart, WeekEnd = weekEnd };

        foreach (var net in nets)
        {
            // NetScheduleRule.DayOfWeek is in UTC; convert each rule to the Eastern
            // day it represents so columns (which are Eastern dates) line up correctly.
            var utcActiveDays = net.ScheduleRules.Select(r => r.DayOfWeek).ToHashSet();
            if (!utcActiveDays.Any()) continue; // holiday-only nets have no fixed day-of-week

            var row = new CalendarNetRow { Net = net };

            for (int i = 0; i < 7; i++)
            {
                // Column dates are Eastern dates
                var nextEasternDate = weekStart.AddDays(i);
                // Convert Eastern date → UTC session date for lookup
                var nextUtcDate = DateConverter.ToUtcSessionDate(nextEasternDate, net.ScheduledTimeUtc);
                // Check if net runs on the corresponding UTC day
                if (!utcActiveDays.Contains(nextUtcDate.DayOfWeek)) continue;
                if (!net.IsInSeasonForDate(nextUtcDate)) continue; // net is out of season

                var prevEasternDate = prevWeekStart.AddDays(i);
                var prevUtcDate = DateConverter.ToUtcSessionDate(prevEasternDate, net.ScheduledTimeUtc);

                var nextSession = allSessions.FirstOrDefault(s => s.NetId == net.Id && s.SessionDate == nextUtcDate);
                var prevSession = allSessions.FirstOrDefault(s => s.NetId == net.Id && s.SessionDate == prevUtcDate);

                var nextCell = ResolveCellFromLoaded(net.Id, nextUtcDate, net.ScheduledTimeUtc, nextSession, standings, unavailabilities);
                var prevCell = ResolveCellFromLoaded(net.Id, prevUtcDate, net.ScheduledTimeUtc, prevSession, standings, unavailabilities);

                // Populate hover-tooltip data for assigned (non-open) cells
                if (nextCell.NetControllerId.HasValue && !nextCell.NeedsNcs)
                {
                    var ncId = nextCell.NetControllerId.Value;
                    if (lastScheduledByController.TryGetValue(ncId, out var lastDate))
                        nextCell.LastScheduledDate = lastDate;
                    if (standingNetsByController.TryGetValue(ncId, out var netNames))
                        nextCell.StandingNetNames = netNames;
                }

                // Populate backup info
                if (nextSession is not null && nextSession.BackupRequested)
                {
                    nextCell.BackupRequested = true;
                    nextCell.BackupCallsigns = nextSession.Assignments
                        .Where(a => a.AssignmentType == AssignmentType.Backup && a.Status != AssignmentStatus.Cancelled)
                        .Select(a => a.NetController.Callsign)
                        .ToList();
                }

                // Flag a change when the effective controller or open/covered state differs
                bool changed = nextCell.Callsign    != prevCell.Callsign
                            || nextCell.NeedsNcs     != prevCell.NeedsNcs
                            || nextCell.HasVolunteer  != prevCell.HasVolunteer;

                nextCell.IsChanged    = changed;
                nextCell.PrevCallsign = prevCell.Callsign;
                nextCell.PrevNeedsNcs = prevCell.NeedsNcs;

                row.Cells[i] = nextCell;
            }

            vm.Rows.Add(row);
        }

        return View(vm);
    }

    // Resolve a single calendar cell from in-memory data (mirrors ScheduleService logic)
    private static CalendarCell ResolveCellFromLoaded(
        int netId, DateOnly date, TimeOnly utcTime, NetSession? session,
        List<StandingAssignment> standings, List<Unavailability> unavailabilities)
    {
        var cell = new CalendarCell { Date = date, SessionId = session?.Id };

        // Explicit assignment (sub or confirmed volunteer) takes priority — skip backups
        var explicit_ = session?.Assignments
            .Where(a => a.Status != AssignmentStatus.Cancelled
                     && a.AssignmentType != AssignmentType.Backup)
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefault();

        if (explicit_ is not null)
        {
            cell.NetControllerId = explicit_.NetControllerId;
            cell.Callsign       = explicit_.NetController.Callsign;
            cell.MemberNumber   = explicit_.NetController.MemberNumber;
            cell.AssignmentType = explicit_.AssignmentType;
            bool isPending = explicit_.AssignmentType == AssignmentType.Volunteer
                          && explicit_.Status == AssignmentStatus.Scheduled;
            cell.HasVolunteer = isPending;
            cell.NeedsNcs     = isPending;
            return cell;
        }

        // Fall back to standing assignment (use Eastern DayOfWeek to match)
        var easternDate = DateConverter.ToEasternDate(date, utcTime);
        var standing = standings.FirstOrDefault(sa =>
            sa.NetId == netId &&
            sa.DayOfWeek == easternDate.DayOfWeek &&
            sa.EffectiveFrom <= date &&
            (sa.EffectiveTo == null || sa.EffectiveTo >= date));

        if (standing is null)
        {
            cell.NeedsNcs = true;
            return cell;
        }

        // Unavailability dates are in Eastern; compare against the Eastern date
        bool unavailable = unavailabilities.Any(u =>
            u.NetControllerId == standing.NetControllerId &&
            u.StartDate <= easternDate && u.EndDate >= easternDate &&
            (u.NetId == null || u.NetId == netId));

        if (unavailable || session?.IsForcedOpen == true)
        {
            cell.NeedsNcs        = true;
            cell.NetControllerId = standing.NetControllerId;
            cell.Callsign        = standing.NetController.Callsign;
            cell.MemberNumber    = standing.NetController.MemberNumber;
            return cell;
        }

        cell.NetControllerId = standing.NetControllerId;
        cell.Callsign        = standing.NetController.Callsign;
        cell.MemberNumber    = standing.NetController.MemberNumber;
        cell.AssignmentType  = AssignmentType.Regular;
        return cell;
    }

    // Returns net IDs this user is allowed to manage (all for SuperAdmin, assigned nets for BandCoordinator)
    private async Task<List<int>> GetManagedNetIdsAsync()
    {
        if (User.IsInRole("SuperAdmin"))
            return await _db.Nets.Where(n => n.IsActive).Select(n => n.Id).ToListAsync();

        var userId = _userManager.GetUserId(User)!;
        var ncId = await _db.NetControllers
            .Where(nc => nc.UserId == userId)
            .Select(nc => nc.Id)
            .FirstOrDefaultAsync();

        var coordId = await _db.BandCoordinators
            .Where(bc => bc.NetControllerId == ncId && bc.IsActive)
            .Select(bc => bc.Id)
            .FirstOrDefaultAsync();

        return await _db.NetCoordinatorAssignments
            .Where(nca => nca.BandCoordinatorId == coordId && nca.EndDate == null)
            .Select(nca => nca.NetId)
            .ToListAsync();
    }
}
