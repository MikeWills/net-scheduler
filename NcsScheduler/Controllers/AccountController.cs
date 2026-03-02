using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.ViewModels;

namespace NcsScheduler.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard", "Schedule");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);
            return RedirectToAction("Dashboard", "Schedule");
        }

        ModelState.AddModelError("", "Invalid email or password.");
        return View(model);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Schedule");
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var user = await _db.Users
            .Include(u => u.NetController)
            .FirstOrDefaultAsync(u => u.Id == _userManager.GetUserId(User));

        if (user is null) return RedirectToAction("Login");

        var nc = user.NetController;

        // Auto-generate an iCal token the first time the profile is visited
        if (nc is not null && string.IsNullOrEmpty(nc.IcalToken))
        {
            nc.IcalToken = Guid.NewGuid().ToString("N"); // 32 hex chars, no dashes
            await _db.SaveChangesAsync();
        }

        var icalUrl = nc?.IcalToken is not null
            ? Url.Action("Feed", "Ical", new { token = nc.IcalToken }, Request.Scheme)
            : null;

        var vm = new ProfileViewModel
        {
            Callsign = nc?.Callsign ?? "",
            MemberNumber = nc?.MemberNumber,
            Name = nc?.Name ?? user.Email ?? "",
            Email = nc?.Email ?? user.Email,
            Phone = nc?.Phone,
            NotifyOnSlotOpened = nc?.NotifyOnSlotOpened ?? false,
            NotifyOnAssigned = nc?.NotifyOnAssigned ?? false,
            IcalFeedUrl = icalUrl
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _db.Users
            .Include(u => u.NetController)
            .FirstOrDefaultAsync(u => u.Id == _userManager.GetUserId(User));

        if (user is null) return RedirectToAction("Login");

        if (user.NetController is not null)
        {
            user.NetController.Name = model.Name;
            user.NetController.Email = model.Email;
            user.NetController.Phone = model.Phone;
            user.NetController.NotifyOnSlotOpened = model.NotifyOnSlotOpened;
            user.NetController.NotifyOnAssigned = model.NotifyOnAssigned;
        }

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError("", error.Description);
                return View(model);
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Profile updated.";
        return RedirectToAction("Profile");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
