using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

[Authorize(Policy = "SuperAdminOnly")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var vm = new AdminDashboardViewModel
        {
            TotalControllers = await _db.NetControllers.CountAsync(nc => nc.IsActive),
            TotalNets = await _db.Nets.CountAsync(n => n.IsActive),
            OpenSlotCount = 0  // simplified for now
        };
        return View(vm);
    }

    // ── User & Role Management ────────────────────────────────────────────────

    public async Task<IActionResult> Users()
    {
        var currentUserId = _userManager.GetUserId(User)!;
        var allUsers = await _userManager.Users
            .Include(u => u.NetController)
            .OrderBy(u => u.NetController != null ? u.NetController.Callsign : u.Email)
            .ToListAsync();

        // Build a set of NetController IDs that have an active BandCoordinator record
        var coordinatorNcIds = await _db.BandCoordinators
            .Where(bc => bc.IsActive)
            .Select(bc => bc.NetControllerId)
            .ToHashSetAsync();

        var vms = new List<UserRoleViewModel>();
        foreach (var user in allUsers)
        {
            var roles = await _userManager.GetRolesAsync(user);
            vms.Add(new UserRoleViewModel
            {
                UserId = user.Id,
                Email = user.Email ?? "",
                Callsign = user.NetController?.Callsign,
                Name = user.NetController?.Name,
                Roles = roles.ToList(),
                IsCurrentUser = user.Id == currentUserId,
                HasCoordinatorRecord = user.NetControllerId.HasValue && coordinatorNcIds.Contains(user.NetControllerId.Value)
            });
        }

        return View(vms);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantAdmin(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        if (!await _userManager.IsInRoleAsync(user, "SuperAdmin"))
            await _userManager.AddToRoleAsync(user, "SuperAdmin");

        TempData["Success"] = $"{user.Email} has been granted Admin access. They must log out and back in for the change to take effect.";
        return RedirectToAction("Users");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GrantCoordinator(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        if (!await _userManager.IsInRoleAsync(user, "BandCoordinator"))
            await _userManager.AddToRoleAsync(user, "BandCoordinator");

        TempData["Success"] = $"Coordinator role granted to {user.Email}. They must log out and back in for the change to take effect.";
        return RedirectToAction("Users");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeAdmin(string userId)
    {
        var currentUserId = _userManager.GetUserId(User)!;
        if (userId == currentUserId)
        {
            TempData["Error"] = "You cannot revoke your own admin access.";
            return RedirectToAction("Users");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        await _userManager.RemoveFromRoleAsync(user, "SuperAdmin");
        TempData["Success"] = $"Admin access revoked for {user.Email}. They must log out and back in for the change to take effect.";
        return RedirectToAction("Users");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoteCoordinator(string userId)
    {
        var currentUserId = _userManager.GetUserId(User)!;
        if (userId == currentUserId)
        {
            TempData["Error"] = "You cannot demote yourself.";
            return RedirectToAction("Users");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        // Remove the role
        await _userManager.RemoveFromRoleAsync(user, "BandCoordinator");

        // Deactivate the BandCoordinator record and end all their net assignments
        if (user.NetControllerId.HasValue)
        {
            var bc = await _db.BandCoordinators
                .FirstOrDefaultAsync(b => b.NetControllerId == user.NetControllerId && b.IsActive);
            if (bc is not null)
            {
                bc.IsActive = false;

                var assignments = await _db.NetCoordinatorAssignments
                    .Where(nca => nca.BandCoordinatorId == bc.Id && nca.EndDate == null)
                    .ToListAsync();
                foreach (var a in assignments)
                    a.EndDate = DateOnly.FromDateTime(DateTime.UtcNow);

                await _db.SaveChangesAsync();
            }
        }

        TempData["Success"] = $"{user.Email} has been demoted from Band Coordinator. They must log out and back in for the change to take effect.";
        return RedirectToAction("Users");
    }

    // ── Standing Assignments ──────────────────────────────────────────────────

    public async Task<IActionResult> StandingAssignments()
    {
        var assignments = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa => sa.EffectiveTo == null)
            .ToListAsync();

        ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
        ViewBag.Controllers = await _db.NetControllers.Where(nc => nc.IsActive).OrderBy(nc => nc.Callsign).ToListAsync();
        ViewBag.Rules = await _db.NetScheduleRules.Where(r => r.IsActive).ToListAsync();
        return View(assignments);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStanding(int netId, int netControllerId, DayOfWeek dayOfWeek)
    {
        // End any current standing assignment for this net+day
        var current = await _db.StandingAssignments
            .Where(sa => sa.NetId == netId && sa.DayOfWeek == dayOfWeek && sa.EffectiveTo == null)
            .ToListAsync();
        foreach (var sa in current) sa.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        if (netControllerId > 0)  // 0 = "-- Unassigned --", just clears the slot
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
        TempData["Success"] = "Standing assignment updated.";
        return RedirectToAction("StandingAssignments");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStanding(int id)
    {
        var sa = await _db.StandingAssignments.FindAsync(id);
        if (sa is not null)
        {
            sa.EffectiveTo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            await _db.SaveChangesAsync();
        }
        TempData["Success"] = "Standing assignment removed.";
        return RedirectToAction("StandingAssignments");
    }

}
