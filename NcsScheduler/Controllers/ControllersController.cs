using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;

namespace NcsScheduler.Controllers;

public class ControllersController : Controller
{
    private readonly ApplicationDbContext _db;

    public ControllersController(ApplicationDbContext db) => _db = db;

    [Authorize(Roles = "SuperAdmin,BandCoordinator")]
    public async Task<IActionResult> Index()
    {
        var controllers = await _db.NetControllers
            .OrderByDescending(nc => nc.IsActive)
            .ThenBy(nc => nc.Callsign)
            .ToListAsync();
        return View(controllers);
    }

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpGet]
    public IActionResult Create() => View(new ControllerEditViewModel());

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ControllerEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        if (await _db.NetControllers.AnyAsync(nc => nc.Callsign == model.Callsign.ToUpper()))
        {
            ModelState.AddModelError("Callsign", "A controller with this callsign already exists.");
            return View(model);
        }

        var nc = new NetController
        {
            Callsign = model.Callsign.ToUpper(),
            MemberNumber = model.MemberNumber,
            Name = model.Name,
            Email = model.Email,
            Phone = model.Phone,
            IsActive = true
        };
        _db.NetControllers.Add(nc);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Controller {nc.Callsign} created.";
        return RedirectToAction("Index");
    }

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var nc = await _db.NetControllers.FirstOrDefaultAsync(nc => nc.Id == id);
        if (nc is null) return NotFound();

        var vm = new ControllerEditViewModel
        {
            Id = nc.Id,
            Callsign = nc.Callsign,
            MemberNumber = nc.MemberNumber,
            Name = nc.Name,
            Email = nc.Email,
            Phone = nc.Phone,
            IsActive = nc.IsActive
        };
        return View(vm);
    }

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ControllerEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var nc = await _db.NetControllers.FirstOrDefaultAsync(nc => nc.Id == model.Id);
        if (nc is null) return NotFound();

        nc.Callsign = model.Callsign.ToUpper();
        nc.MemberNumber = model.MemberNumber;
        nc.Name = model.Name;
        nc.Email = model.Email;
        nc.Phone = model.Phone;
        nc.IsActive = model.IsActive;

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Controller {nc.Callsign} updated.";
        return RedirectToAction("Index");
    }

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        var nc = await _db.NetControllers.FindAsync(id);
        if (nc is null) return NotFound();

        nc.IsActive = active;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Controller {nc.Callsign} {(active ? "activated" : "deactivated")}.";
        return RedirectToAction("Index");
    }
}
