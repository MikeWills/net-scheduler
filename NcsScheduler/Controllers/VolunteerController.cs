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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Open sessions across all active nets — any NCS can run any net
        var openSessions = await _db.NetSessions
            .Include(s => s.Net)
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
            .Where(s => s.Net.IsActive && s.SessionDate >= today)
            .OrderBy(s => s.SessionDate)
            .ToListAsync();

        var allStanding = await _db.StandingAssignments
            .Where(sa => sa.EffectiveTo == null || sa.EffectiveTo >= today).ToListAsync();
        var allUnavailable = await _db.Unavailabilities
            .Where(u => u.EndDate >= today).ToListAsync();

        var openSlots = openSessions.Where(session =>
        {
            if (!session.Net.IsInSeasonForDate(session.SessionDate)) return false;
            if (session.Assignments.Any(a => a.AssignmentType != AssignmentType.Volunteer || a.Status == AssignmentStatus.Confirmed))
                return false;
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

        ViewBag.MyControllerId = user.NetController.Id;
        return View(openSlots);
    }

    // TODO: Add a "backup request" feature. The assigned NCS for a session should be able to flag
    // that they may need a backup (e.g. not 100% sure they can make it). This would:
    // - Add a BackupRequested flag (or status) to SessionAssignment
    // - Send an email notification to NCS members (similar to open-slot notifications) inviting
    //   a couple of volunteers to stand by as backup — not to replace the NCS, just to be on call
    // - Show a "Stand By as Backup" button on the dashboard Open Slots card and/or volunteer page
    // - Store backup volunteers as a new AssignmentType (e.g. AssignmentType.Backup) so they are
    //   distinct from full volunteers; the NCS and backups coordinate amongst themselves
    // - Cap the number of accepted backups (e.g. 2) to avoid over-requesting
    // - No BC involvement required — this is self-managed by the net controllers

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
}
