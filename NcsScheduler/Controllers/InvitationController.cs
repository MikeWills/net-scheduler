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

    [Authorize(Policy = "CanManageControllers")]
    [HttpGet]
    public async Task<IActionResult> Send()
    {
        ViewBag.Controllers = await _db.NetControllers
            .Where(nc => nc.UserId == null && nc.IsActive)
            .OrderBy(nc => nc.Callsign)
            .ToListAsync();
        return View(new InviteViewModel());
    }

    [Authorize(Policy = "CanManageControllers")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(InviteViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Controllers = await _db.NetControllers
                .Where(nc => nc.UserId == null && nc.IsActive)
                .OrderBy(nc => nc.Callsign).ToListAsync();
            return View(model);
        }

        var nc = await _db.NetControllers.FindAsync(model.NetControllerId);
        if (nc is null) { ModelState.AddModelError("", "Controller not found."); return View(model); }

        var token = Guid.NewGuid().ToString("N");
        var invite = new Invitation
        {
            Email = model.Email,
            Token = token,
            InvitedByUserId = _userManager.GetUserId(User)!,
            NetControllerId = nc.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        // Update email on controller record too
        nc.Email = model.Email;
        _db.Invitations.Add(invite);
        await _db.SaveChangesAsync();

        var inviteUrl = Url.Action("Accept", "Invitation", new { token }, Request.Scheme)!;
        await _emailService.SendInviteAsync(model.Email, nc.Name, inviteUrl);

        TempData["Success"] = $"Invitation sent to {model.Email}.";
        return RedirectToAction("Send");
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
