using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;

namespace NcsScheduler.Controllers;

[Authorize(Policy = "SuperAdminOnly")]
public class CoordinatorsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IServiceProvider _services;

    public CoordinatorsController(ApplicationDbContext db, IServiceProvider services)
    {
        _db = db;
        _services = services;
    }

    public async Task<IActionResult> Index()
    {
        var coordinators = await _db.BandCoordinators
            .Include(bc => bc.NetController)
            .Include(bc => bc.NetAssignments.Where(nca => nca.EndDate == null))
                .ThenInclude(nca => nca.Net)
            .OrderBy(bc => bc.NetController.Callsign)
            .ToListAsync();

        // Only show controllers not already promoted
        var promotedIds = coordinators.Select(bc => bc.NetControllerId).ToHashSet();
        ViewBag.EligibleControllers = await _db.NetControllers
            .Where(nc => nc.IsActive && !promotedIds.Contains(nc.Id))
            .OrderBy(nc => nc.Callsign)
            .ToListAsync();

        ViewBag.Nets = await _db.Nets
            .Where(n => n.IsActive)
            .OrderBy(n => n.Name)
            .ToListAsync();

        return View(coordinators);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Promote(int netControllerId)
    {
        var nc = await _db.NetControllers.FindAsync(netControllerId);
        if (nc is null) return NotFound();

        if (!await _db.BandCoordinators.AnyAsync(bc => bc.NetControllerId == netControllerId))
        {
            _db.BandCoordinators.Add(new BandCoordinator { NetControllerId = netControllerId, IsActive = true });
            await _db.SaveChangesAsync();

            // Add BandCoordinator role to user if they have an account
            if (nc.UserId is not null)
            {
                var userManager = _services.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
                var user = await userManager.FindByIdAsync(nc.UserId);
                if (user is not null && !await userManager.IsInRoleAsync(user, "BandCoordinator"))
                    await userManager.AddToRoleAsync(user, "BandCoordinator");
            }
            TempData["Success"] = $"{nc.Callsign} promoted to Band Coordinator.";
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignNet(int bandCoordinatorId, int netId)
    {
        // Only add if this coordinator doesn't already manage this net
        var alreadyAssigned = await _db.NetCoordinatorAssignments
            .AnyAsync(nca => nca.BandCoordinatorId == bandCoordinatorId && nca.NetId == netId && nca.EndDate == null);

        if (!alreadyAssigned)
        {
            // End any existing assignment for this net from a *different* coordinator
            var current = await _db.NetCoordinatorAssignments
                .Where(nca => nca.NetId == netId && nca.EndDate == null && nca.BandCoordinatorId != bandCoordinatorId)
                .ToListAsync();
            foreach (var nca in current) nca.EndDate = DateOnly.FromDateTime(DateTime.UtcNow);

            _db.NetCoordinatorAssignments.Add(new NetCoordinatorAssignment
            {
                NetId = netId,
                BandCoordinatorId = bandCoordinatorId,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Net added to coordinator.";
        }

        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveNet(int bandCoordinatorId, int netId)
    {
        var assignment = await _db.NetCoordinatorAssignments
            .FirstOrDefaultAsync(nca => nca.BandCoordinatorId == bandCoordinatorId && nca.NetId == netId && nca.EndDate == null);

        if (assignment is not null)
        {
            assignment.EndDate = DateOnly.FromDateTime(DateTime.UtcNow);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Net removed from coordinator.";
        }

        return RedirectToAction("Index");
    }
}
