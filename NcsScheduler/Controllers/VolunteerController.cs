using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

[Authorize]
public class VolunteerController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;

    public VolunteerController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailService emailService)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return RedirectToAction("Index", "Schedule");

        var nc = user.NetController;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Net preferences for this controller (empty = all nets)
        var myPreferenceIds = (await _db.NetPreferences
            .Where(p => p.NetControllerId == nc.Id)
            .Select(p => p.NetId)
            .ToListAsync())
            .ToHashSet();

        // Open sessions across all active nets — any NCS can run any net
        var openSessions = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
                .ThenInclude(a => a.NetController)
            .Where(s => s.Net.IsActive && s.SessionDate >= today)
            .OrderBy(s => s.SessionDate)
            .ToListAsync();

        var allStanding = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa => sa.EffectiveTo == null || sa.EffectiveTo >= today).ToListAsync();
        var allUnavailable = await _db.Unavailabilities
            .Where(u => u.EndDate >= today).ToListAsync();

        var openSlots = openSessions.Where(session =>
        {
            if (!session.Net.IsInSeasonForDate(session.SessionDate)) return false;
            if (session.Assignments.Any(a => a.AssignmentType != AssignmentType.Volunteer || a.Status == AssignmentStatus.Confirmed))
                return false;
            if (myPreferenceIds.Count > 0 && !myPreferenceIds.Contains(session.NetId)) return false;
            if (session.IsForcedOpen) return true;
            var standing = allStanding.FirstOrDefault(sa =>
                sa.NetId == session.NetId &&
                sa.DayOfWeek == session.SessionDate.DayOfWeek &&
                sa.EffectiveFrom <= session.SessionDate &&
                (sa.EffectiveTo == null || sa.EffectiveTo >= session.SessionDate));
            if (standing is null) return true;
            return allUnavailable.Any(u =>
                u.NetControllerId == standing.NetControllerId &&
                u.StartDate <= session.SessionDate && u.EndDate >= session.SessionDate &&
                (u.NetId == null || u.NetId == session.NetId));
        }).ToList();

        // Backup requests: sessions where BackupRequested = true, I'm not the requesting NCS,
        // and there's still room (< 2 backups) or I'm already standing by.
        // openSessions already covers all active-net sessions from today, so filter from there.
        var backupSessions = new List<NetSession>();
        foreach (var session in openSessions.Where(s => s.BackupRequested))
        {
            if (myPreferenceIds.Count > 0 && !myPreferenceIds.Contains(session.NetId)) continue;

            var backups = session.Assignments
                .Where(a => a.AssignmentType == AssignmentType.Backup && a.Status != AssignmentStatus.Cancelled)
                .ToList();
            bool alreadyStandingBy = backups.Any(a => a.NetControllerId == nc.Id);
            if (backups.Count >= 2 && !alreadyStandingBy) continue;

            var requestingAssignment = session.Assignments
                .FirstOrDefault(a => a.AssignmentType != AssignmentType.Backup
                                  && a.AssignmentType != AssignmentType.Volunteer
                                  && a.Status != AssignmentStatus.Cancelled);
            var standingNcs = allStanding.FirstOrDefault(sa =>
                sa.NetId == session.NetId &&
                sa.DayOfWeek == session.SessionDate.DayOfWeek &&
                sa.EffectiveFrom <= session.SessionDate &&
                (sa.EffectiveTo == null || sa.EffectiveTo >= session.SessionDate));

            bool iAmTheNcs = requestingAssignment?.NetControllerId == nc.Id
                || (requestingAssignment is null && standingNcs?.NetControllerId == nc.Id);
            if (iAmTheNcs) continue;

            backupSessions.Add(session);
        }

        ViewBag.MyControllerId = nc.Id;
        ViewBag.BackupSessions = backupSessions;
        ViewBag.AllStanding = allStanding;
        return View(openSlots);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Volunteer(int sessionId)
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return Forbid();

        var session = await _db.NetSessions.Include(s => s.Net).FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return NotFound();

        // Prevent duplicate volunteers
        var existing = await _db.SessionAssignments.AnyAsync(a =>
            a.NetSessionId == sessionId &&
            a.NetControllerId == user.NetController.Id &&
            a.Status != AssignmentStatus.Cancelled);
        if (existing)
        {
            TempData["Error"] = "You have already volunteered for this slot.";
            return RedirectToAction("Index");
        }

        _db.SessionAssignments.Add(new SessionAssignment
        {
            NetSessionId = sessionId,
            NetControllerId = user.NetController.Id,
            AssignmentType = AssignmentType.Volunteer,
            Status = AssignmentStatus.Scheduled,
            AssignedByUserId = userId
        });
        await _db.SaveChangesAsync();

        // Notify coordinator
        var coord = await _db.NetCoordinatorAssignments
            .Include(nca => nca.BandCoordinator).ThenInclude(bc => bc.NetController)
            .Where(nca => nca.NetId == session.NetId && nca.EndDate == null)
            .Select(nca => nca.BandCoordinator.NetController)
            .FirstOrDefaultAsync();

        if (coord?.NotifyOnSlotOpened == true)
            await _emailService.SendVolunteerNotificationAsync(coord, session, user.NetController);

        TempData["Success"] = $"You have volunteered for {session.Net?.Name} on {session.SessionDate:MMMM d, yyyy}.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Flag a session as needing a backup. Sends email to NCS members who have opted in.
    /// Only the assigned NCS for the session may request a backup.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestBackup(int sessionId)
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return Forbid();

        var nc = user.NetController;
        var session = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return NotFound();

        // Verify the requesting user is the assigned NCS (via explicit or standing assignment)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        bool isAssignedNcs = session.Assignments.Any(a =>
            a.NetControllerId == nc.Id &&
            a.AssignmentType != AssignmentType.Volunteer &&
            a.AssignmentType != AssignmentType.Backup);

        if (!isAssignedNcs)
        {
            var standing = await _db.StandingAssignments
                .FirstOrDefaultAsync(sa =>
                    sa.NetId == session.NetId &&
                    sa.NetControllerId == nc.Id &&
                    sa.DayOfWeek == session.SessionDate.DayOfWeek &&
                    sa.EffectiveFrom <= session.SessionDate &&
                    (sa.EffectiveTo == null || sa.EffectiveTo >= session.SessionDate));
            isAssignedNcs = standing is not null;
        }

        if (!isAssignedNcs)
        {
            TempData["Error"] = "You can only request a backup for a session you are assigned to run.";
            return RedirectToAction("Dashboard", "Schedule");
        }

        session.BackupRequested = true;
        await _db.SaveChangesAsync();

        // Send email to NCS members who have opted in (excluding the requesting NCS)
        var recipients = await _db.NetControllers
            .Where(c => c.IsActive && c.NotifyOnSlotOpened && c.Id != nc.Id)
            .ToListAsync();

        await _emailService.SendBackupRequestAsync(recipients, session, nc);

        TempData["Success"] = $"Backup requested for {session.Net?.Name} on {session.SessionDate:MMMM d, yyyy}. An email has been sent to available NCS members.";
        return RedirectToAction("Dashboard", "Schedule");
    }

    /// <summary>Cancel a previously requested backup (when the NCS is confident they can make it).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelBackupRequest(int sessionId)
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return Forbid();

        var nc = user.NetController;
        var session = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return NotFound();

        // Verify the requesting user is the assigned NCS
        bool isAssignedNcs = session.Assignments.Any(a =>
            a.NetControllerId == nc.Id &&
            a.AssignmentType != AssignmentType.Volunteer &&
            a.AssignmentType != AssignmentType.Backup);

        if (!isAssignedNcs)
        {
            var standing = await _db.StandingAssignments
                .FirstOrDefaultAsync(sa =>
                    sa.NetId == session.NetId &&
                    sa.NetControllerId == nc.Id &&
                    sa.DayOfWeek == session.SessionDate.DayOfWeek &&
                    sa.EffectiveFrom <= session.SessionDate &&
                    (sa.EffectiveTo == null || sa.EffectiveTo >= session.SessionDate));
            isAssignedNcs = standing is not null;
        }

        if (!isAssignedNcs)
        {
            TempData["Error"] = "You can only cancel a backup request for a session you are assigned to run.";
            return RedirectToAction("Dashboard", "Schedule");
        }

        session.BackupRequested = false;

        // Cancel all pending backup assignments
        var backups = session.Assignments
            .Where(a => a.AssignmentType == AssignmentType.Backup)
            .ToList();
        foreach (var b in backups)
            b.Status = AssignmentStatus.Cancelled;

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Backup request cancelled for {session.Net?.Name} on {session.SessionDate:MMMM d, yyyy}.";
        return RedirectToAction("Dashboard", "Schedule");
    }

    /// <summary>Stand by as a backup for a session that has BackupRequested = true.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StandByAsBackup(int sessionId, string returnTo = "Dashboard")
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return Forbid();

        var nc = user.NetController;
        var session = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
            .FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return NotFound();

        if (!session.BackupRequested)
        {
            TempData["Error"] = "This session is no longer requesting a backup.";
            return RedirectToReturn(returnTo);
        }

        // Cap at 2 backups
        var existingBackups = session.Assignments
            .Count(a => a.AssignmentType == AssignmentType.Backup);
        if (existingBackups >= 2)
        {
            TempData["Error"] = "This session already has enough backups standing by.";
            return RedirectToReturn(returnTo);
        }

        // Prevent duplicate backup entries
        bool alreadyBackup = session.Assignments.Any(a =>
            a.NetControllerId == nc.Id && a.AssignmentType == AssignmentType.Backup);
        if (alreadyBackup)
        {
            TempData["Error"] = "You are already standing by as backup for this session.";
            return RedirectToReturn(returnTo);
        }

        _db.SessionAssignments.Add(new SessionAssignment
        {
            NetSessionId = sessionId,
            NetControllerId = nc.Id,
            AssignmentType = AssignmentType.Backup,
            Status = AssignmentStatus.Scheduled,
            AssignedByUserId = userId
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"You are now standing by as backup for {session.Net?.Name} on {session.SessionDate:MMMM d, yyyy}.";
        return RedirectToReturn(returnTo);
    }

    private IActionResult RedirectToReturn(string returnTo) =>
        returnTo == "Volunteer"
            ? RedirectToAction("Index")
            : RedirectToAction("Dashboard", "Schedule");
}
