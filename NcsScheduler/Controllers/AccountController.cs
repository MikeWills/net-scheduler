using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NcsScheduler.Data;
using NcsScheduler.Models.ViewModels;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly AppSettings _appSettings;
    private readonly IEmailService _emailService;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IOptions<AppSettings> appSettings,
        IEmailService emailService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
        _appSettings = appSettings.Value;
        _emailService = emailService;
    }

    /// <summary>
    /// Builds an absolute URL for the iCal feed. Uses App:BaseUrl from configuration
    /// when set (production), otherwise falls back to the current request's host (development).
    /// </summary>
    private string? BuildIcalUrl(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        var baseUrl = _appSettings.BaseUrl?.TrimEnd('/');
        if (!string.IsNullOrEmpty(baseUrl))
            return $"{baseUrl}/Ical/Feed/{token}";

        return Url.Action("Feed", "Ical", new { token }, Request.Scheme);
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

        var icalUrl = BuildIcalUrl(nc?.IcalToken);

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

    // ── Forgot Password ──────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Always show the confirmation page — don't reveal whether the email exists
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is not null)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // Build reset URL (respects App:BaseUrl in production)
            string resetUrl;
            var baseUrl = _appSettings.BaseUrl?.TrimEnd('/');
            if (!string.IsNullOrEmpty(baseUrl))
                resetUrl = $"{baseUrl}/Account/ResetPassword?email={Uri.EscapeDataString(model.Email)}&token={Uri.EscapeDataString(token)}";
            else
                resetUrl = Url.Action("ResetPassword", "Account",
                    new { email = model.Email, token }, Request.Scheme)!;

            // Resolve display name: use NetController name if linked, else email
            var nc = user.NetControllerId.HasValue
                ? await _db.NetControllers.FindAsync(user.NetControllerId.Value)
                : null;
            var displayName = nc?.Name ?? model.Email;

            await _emailService.SendPasswordResetAsync(model.Email, displayName, resetUrl);
        }

        return RedirectToAction("ForgotPasswordConfirmation");
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation() => View();

    // ── Reset Password ───────────────────────────────────────────────────────

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            return RedirectToAction("Login");

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            // Don't reveal that the user doesn't exist
            TempData["Success"] = "Password has been reset. You can now log in.";
            return RedirectToAction("Login");
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        if (result.Succeeded)
        {
            TempData["Success"] = "Password has been reset. You can now log in.";
            return RedirectToAction("Login");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);

        return View(model);
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
