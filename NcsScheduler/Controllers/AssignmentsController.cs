using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
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

    public AssignmentsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailService emailService)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
    }

    // GET: Assignments/Index
    // Section 1 — Regular Schedule grid (standing assignments, repeat weekly)
    // Section 2 — Upcoming exceptions (open slots / volunteers needing a sub)
    public async Task<IActionResult> Index()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var exceptionWindow = today.AddDays(28); // 4-week lookahead for exceptions

        var managedNetIds = await GetManagedNetIdsAsync();

        // ── Regular Schedule ──────────────────────────────────────────────────
        var standings = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa => sa.EffectiveTo == null && managedNetIds.Contains(sa.NetId))
            .ToListAsync();

        var rules = await _db.NetScheduleRules
            .Where(r => r.IsActive && managedNetIds.Contains(r.NetId))
            .ToListAsync();

        var nets = await _db.Nets
            .Where(n => n.IsActive && managedNetIds.Contains(n.Id))
            .OrderBy(n => n.Name)
            .ToListAsync();

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

            standingMap.TryGetValue((session.NetId, session.SessionDate.DayOfWeek), out var standing);

            bool regularUnavailable = standing is not null && unavailabilities.Any(u =>
                u.NetControllerId == standing.NetControllerId &&
                u.StartDate <= session.SessionDate && u.EndDate >= session.SessionDate &&
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

        ViewBag.Nets = nets;
        ViewBag.Controllers = controllers;
        ViewBag.Rules = rules;
        ViewBag.Standings = standings;
        ViewBag.Exceptions = exceptions;
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
    public async Task<IActionResult> Assign(int sessionId, int controllerId)
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
        await _db.SaveChangesAsync();

        if (controller.NotifyOnAssigned)
            await _emailService.SendAssignmentConfirmationAsync(controller, session);

        TempData["Success"] = $"Assigned {controller.Callsign} to {session.Net?.Name} on {session.SessionDate:MMMM d, yyyy}.";
        return RedirectToAction("Index");
    }

    // POST: Confirm — promote a volunteer to confirmed for a specific session
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int assignmentId)
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
        return RedirectToAction("Index");
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

        // Find or create the session for this net+date
        var session = await _db.NetSessions
            .FirstOrDefaultAsync(s => s.NetId == netId && s.SessionDate == sessionDate);

        if (session is null)
        {
            session = new NetSession
            {
                NetId = netId,
                SessionDate = sessionDate,
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

        // Find or create the session
        var session = await _db.NetSessions
            .FirstOrDefaultAsync(s => s.NetId == netId && s.SessionDate == sessionDate);

        if (session is null)
        {
            session = new NetSession
            {
                NetId = netId,
                SessionDate = sessionDate,
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
        TempData["Success"] = $"{net.Name} on {sessionDate:MMMM d, yyyy} has been marked as open — it will show as NEED NCS.";
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

        TempData["Success"] = $"Open status cleared for {session.Net?.Name} on {session.SessionDate:MMMM d, yyyy}.";
        return RedirectToAction("Index");
    }

    // GET: Assignments/Calendar — weekly grid for the next Sun–Sat
    public async Task<IActionResult> Calendar()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // "Next week" = the Sun–Sat block that starts after this week's Sunday.
        // e.g. if today is Sat Feb 28, this week's Sunday is Feb 22, next Sunday is Mar 1.
        int daysSinceSunday = (int)today.DayOfWeek; // Sunday=0 … Saturday=6
        var thisWeekSunday  = today.AddDays(-daysSinceSunday);
        var weekStart       = thisWeekSunday.AddDays(7);  // next Sunday
        var weekEnd         = weekStart.AddDays(6);        // next Saturday
        var prevWeekStart   = thisWeekSunday;              // this Sunday (for comparison)

        var managedNetIds = await GetManagedNetIdsAsync();

        var nets = await _db.Nets
            .Include(n => n.ScheduleRules.Where(r => r.IsActive))
            .Where(n => n.IsActive && managedNetIds.Contains(n.Id))
            .OrderBy(n => n.ScheduledTimeUtc).ThenBy(n => n.Name)
            .ToListAsync();

        // Sessions for both weeks in one query
        var allSessions = await _db.NetSessions
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
                .ThenInclude(a => a.NetController)
            .Where(s => managedNetIds.Contains(s.NetId)
                     && s.SessionDate >= prevWeekStart
                     && s.SessionDate <= weekEnd)
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

        var vm = new BcCalendarViewModel { WeekStart = weekStart, WeekEnd = weekEnd };

        foreach (var net in nets)
        {
            var activeDays = net.ScheduleRules.Select(r => r.DayOfWeek).ToHashSet();
            if (!activeDays.Any()) continue; // holiday-only nets have no fixed day-of-week

            var row = new CalendarNetRow { Net = net };

            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)i;
                if (!activeDays.Contains(dow)) continue; // net doesn't run this day

                var nextDate = weekStart.AddDays(i);
                var prevDate = prevWeekStart.AddDays(i);

                var nextSession = allSessions.FirstOrDefault(s => s.NetId == net.Id && s.SessionDate == nextDate);
                var prevSession = allSessions.FirstOrDefault(s => s.NetId == net.Id && s.SessionDate == prevDate);

                var nextCell = ResolveCellFromLoaded(net.Id, nextDate, nextSession, standings, unavailabilities);
                var prevCell = ResolveCellFromLoaded(net.Id, prevDate, prevSession, standings, unavailabilities);

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
        int netId, DateOnly date, NetSession? session,
        List<StandingAssignment> standings, List<Unavailability> unavailabilities)
    {
        var cell = new CalendarCell { Date = date, SessionId = session?.Id };

        // Explicit assignment (sub or confirmed volunteer) takes priority
        var explicit_ = session?.Assignments
            .Where(a => a.Status != AssignmentStatus.Cancelled)
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefault();

        if (explicit_ is not null)
        {
            cell.Callsign       = explicit_.NetController.Callsign;
            cell.MemberNumber   = explicit_.NetController.MemberNumber;
            cell.AssignmentType = explicit_.AssignmentType;
            bool isPending = explicit_.AssignmentType == AssignmentType.Volunteer
                          && explicit_.Status == AssignmentStatus.Scheduled;
            cell.HasVolunteer = isPending;
            cell.NeedsNcs     = isPending;
            return cell;
        }

        // Fall back to standing assignment
        var standing = standings.FirstOrDefault(sa =>
            sa.NetId == netId &&
            sa.DayOfWeek == date.DayOfWeek &&
            sa.EffectiveFrom <= date &&
            (sa.EffectiveTo == null || sa.EffectiveTo >= date));

        if (standing is null)
        {
            cell.NeedsNcs = true;
            return cell;
        }

        bool unavailable = unavailabilities.Any(u =>
            u.NetControllerId == standing.NetControllerId &&
            u.StartDate <= date && u.EndDate >= date &&
            (u.NetId == null || u.NetId == netId));

        if (unavailable || session?.IsForcedOpen == true)
        {
            cell.NeedsNcs     = true;
            cell.Callsign     = standing.NetController.Callsign;
            cell.MemberNumber = standing.NetController.MemberNumber;
            return cell;
        }

        cell.Callsign       = standing.NetController.Callsign;
        cell.MemberNumber   = standing.NetController.MemberNumber;
        cell.AssignmentType = AssignmentType.Regular;
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
