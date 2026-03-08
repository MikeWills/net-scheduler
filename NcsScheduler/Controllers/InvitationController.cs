using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

public class InvitationController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;

    public InvitationController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IEmailService emailService)
    {
        _db = db;
        _userManager = userManager;
        _emailService = emailService;
    }

    /// <summary>
    /// Sends an invite to the NCS using the email address already on their record.
    /// Called inline from the Net Controllers list.
    /// </summary>
    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToController(int id)
    {
        var nc = await _db.NetControllers.FindAsync(id);
        if (nc is null) return NotFound();

        if (string.IsNullOrWhiteSpace(nc.Email))
        {
            TempData["Error"] = $"No email on file for {nc.Callsign}. Edit the controller record first.";
            return RedirectToAction("Index", "Controllers");
        }

        if (nc.UserId is not null)
        {
            TempData["Error"] = $"{nc.Callsign} already has an account.";
            return RedirectToAction("Index", "Controllers");
        }

        var token = Guid.NewGuid().ToString("N");
        var invite = new Invitation
        {
            Email = nc.Email,
            Token = token,
            InvitedByUserId = _userManager.GetUserId(User)!,
            NetControllerId = nc.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        _db.Invitations.Add(invite);
        await _db.SaveChangesAsync();

        var inviteUrl = Url.Action("Accept", "Invitation", new { token }, Request.Scheme)!;
        await _emailService.SendInviteAsync(nc.Email, nc.Name, inviteUrl);

        TempData["Success"] = $"Invitation sent to {nc.Email}.";
        return RedirectToAction("Index", "Controllers");
    }

    [HttpGet]
    public async Task<IActionResult> Accept(string token)
    {
        var invite = await _db.Invitations
            .Include(i => i.NetController)
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invite is null || !invite.IsValid)
        {
            TempData["Error"] = "This invitation is invalid or has expired.";
            return RedirectToAction("Login", "Account");
        }

        var vm = new AcceptInviteViewModel
        {
            Token = token,
            Email = invite.Email,
            Callsign = invite.NetController.Callsign,
            Name = invite.NetController.Name
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(AcceptInviteViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var invite = await _db.Invitations
            .Include(i => i.NetController)
            .FirstOrDefaultAsync(i => i.Token == model.Token);

        if (invite is null || !invite.IsValid)
        {
            TempData["Error"] = "This invitation is invalid or has expired.";
            return RedirectToAction("Login", "Account");
        }

        var user = new ApplicationUser
        {
            UserName = invite.Email,
            Email = invite.Email,
            EmailConfirmed = true,
            NetControllerId = invite.NetController.Id
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, "NetController");

        // If this controller was already promoted to Band Coordinator, grant that role now
        var isCoordinator = await _db.BandCoordinators
            .AnyAsync(bc => bc.NetControllerId == invite.NetController.Id && bc.IsActive);
        if (isCoordinator)
            await _userManager.AddToRoleAsync(user, "BandCoordinator");

        invite.NetController.UserId = user.Id;
        invite.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Account created! Please log in.";
        return RedirectToAction("Login", "Account");
    }
}
