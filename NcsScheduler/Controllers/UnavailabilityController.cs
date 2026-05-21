using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Helpers;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

[Authorize]
public class UnavailabilityController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;

    public UnavailabilityController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailService emailService)
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

        var items = await _db.Unavailabilities
            .Include(u => u.Net)
            .Where(u => u.NetControllerId == user.NetController.Id && u.EndDate >= today)
            .OrderBy(u => u.StartDate)
            .ToListAsync();

        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
        ViewBag.Nets = nets;
        return View(new UnavailabilityCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UnavailabilityCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
            return View(model);
        }

        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return RedirectToAction("Index", "Schedule");

        var unavailability = new Unavailability
        {
            NetControllerId = user.NetController.Id,
            NetId = model.NetId,
            StartDate = model.StartDate,
            EndDate = model.EndDate,
            Reason = model.Reason,
            MarkedByUserId = userId
        };
        _db.Unavailabilities.Add(unavailability);
        await _db.SaveChangesAsync();

        // Notify coordinators for affected nets
        await NotifyCoordinatorsAsync(user.NetController, unavailability);

        var label = model.StartDate == model.EndDate
            ? model.StartDate.ToString("MMMM d, yyyy")
            : $"{model.StartDate:MMMM d} – {model.EndDate:MMMM d, yyyy}";
        TempData["Success"] = $"Marked unavailable for {label}.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return RedirectToAction("Index", "Schedule");

        var item = await _db.Unavailabilities.FindAsync(id);
        if (item is null || item.NetControllerId != user.NetController.Id)
            return Forbid();

        ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
        var model = new UnavailabilityEditViewModel
        {
            StartDate = item.StartDate,
            EndDate   = item.EndDate,
            NetId     = item.NetId,
            Reason    = item.Reason
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UnavailabilityEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
            return View(model);
        }

        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);
        if (user?.NetController is null) return RedirectToAction("Index", "Schedule");

        var item = await _db.Unavailabilities.FindAsync(id);
        if (item is null || item.NetControllerId != user.NetController.Id)
            return Forbid();

        item.StartDate = model.StartDate;
        item.EndDate   = model.EndDate;
        item.NetId     = model.NetId;
        item.Reason    = model.Reason;
        await _db.SaveChangesAsync();

        var label = item.StartDate == item.EndDate
            ? item.StartDate.ToString("MMMM d, yyyy")
            : $"{item.StartDate:MMMM d} – {item.EndDate:MMMM d, yyyy}";
        TempData["Success"] = $"Unavailability updated for {label}.";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = _userManager.GetUserId(User)!;
        var user = await _db.Users.Include(u => u.NetController).FirstOrDefaultAsync(u => u.Id == userId);

        var item = await _db.Unavailabilities.FindAsync(id);
        if (item is null || item.NetControllerId != user?.NetController?.Id)
            return Forbid();

        _db.Unavailabilities.Remove(item);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Unavailability removed.";
        return RedirectToAction("Index");
    }

    private async Task NotifyCoordinatorsAsync(NetController controller, Unavailability unavailability)
    {
        // Find net IDs affected by this range
        var netIds = unavailability.NetId.HasValue
            ? new List<int> { unavailability.NetId.Value }
            : await _db.StandingAssignments
                .Where(sa => sa.NetControllerId == controller.Id &&
                             (sa.EffectiveTo == null || sa.EffectiveTo >= unavailability.StartDate))
                .Select(sa => sa.NetId)
                .Distinct().ToListAsync();

        // Find all sessions in the date range for the affected nets.
        // Unavailability dates are in Eastern but SessionDate is UTC; for overnight
        // nets (e.g. 03:00z Mon = Sun evening ET) the UTC date is one day ahead,
        // so extend the query window by +1 day to catch those sessions.
        var sessions = await _db.NetSessions
            .Include(s => s.Net)
            .Where(s => netIds.Contains(s.NetId)
                     && s.SessionDate >= unavailability.StartDate
                     && s.SessionDate <= unavailability.EndDate.AddDays(1))
            .ToListAsync();

        // Filter in memory using Eastern date comparison for accuracy
        sessions = sessions.Where(s =>
        {
            var easternDate = DateConverter.ToEasternDate(s.SessionDate, s.ScheduledTimeUtc);
            return easternDate >= unavailability.StartDate && easternDate <= unavailability.EndDate;
        }).ToList();

        // Notify each affected net's coordinator once per net
        var notifiedNets = new HashSet<int>();
        foreach (var session in sessions)
        {
            if (!notifiedNets.Add(session.NetId)) continue;

            var coord = await _db.NetCoordinatorAssignments
                .Include(nca => nca.BandCoordinator).ThenInclude(bc => bc.NetController)
                .Where(nca => nca.NetId == session.NetId && nca.EndDate == null)
                .Select(nca => nca.BandCoordinator.NetController)
                .FirstOrDefaultAsync();

            if (coord?.NotifyOnSlotOpened == true)
                await _emailService.SendSlotOpenedAsync(coord, session, controller);
        }
    }
}
