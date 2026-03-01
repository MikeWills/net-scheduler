using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;

namespace NcsScheduler.Controllers;

[Authorize(Policy = "CanManageControllers")]
public class ControllersController : Controller
{
    private readonly ApplicationDbContext _db;

    public ControllersController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var controllers = await _db.NetControllers
            .Include(nc => nc.PoolMemberships).ThenInclude(pm => pm.Net)
            .OrderBy(nc => nc.Callsign)
            .ToListAsync();
        return View(controllers);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
        return View(new ControllerEditViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ControllerEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
            return View(model);
        }

        if (await _db.NetControllers.AnyAsync(nc => nc.Callsign == model.Callsign.ToUpper()))
        {
            ModelState.AddModelError("Callsign", "A controller with this callsign already exists.");
            ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
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

        foreach (var netId in model.SelectedNetIds)
            _db.NetControllerPool.Add(new NetControllerPool { NetId = netId, NetControllerId = nc.Id, IsActive = true });

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Controller {nc.Callsign} created.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var nc = await _db.NetControllers
            .Include(nc => nc.PoolMemberships)
            .FirstOrDefaultAsync(nc => nc.Id == id);
        if (nc is null) return NotFound();

        ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
        var vm = new ControllerEditViewModel
        {
            Id = nc.Id,
            Callsign = nc.Callsign,
            MemberNumber = nc.MemberNumber,
            Name = nc.Name,
            Email = nc.Email,
            Phone = nc.Phone,
            IsActive = nc.IsActive,
            SelectedNetIds = nc.PoolMemberships.Where(p => p.IsActive).Select(p => p.NetId).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ControllerEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Nets = await _db.Nets.Where(n => n.IsActive).OrderBy(n => n.Name).ToListAsync();
            return View(model);
        }

        var nc = await _db.NetControllers
            .Include(nc => nc.PoolMemberships)
            .FirstOrDefaultAsync(nc => nc.Id == model.Id);
        if (nc is null) return NotFound();

        nc.Callsign = model.Callsign.ToUpper();
        nc.MemberNumber = model.MemberNumber;
        nc.Name = model.Name;
        nc.Email = model.Email;
        nc.Phone = model.Phone;
        nc.IsActive = model.IsActive;

        // Update pool memberships
        foreach (var pm in nc.PoolMemberships) pm.IsActive = false;
        foreach (var netId in model.SelectedNetIds)
        {
            var existing = nc.PoolMemberships.FirstOrDefault(p => p.NetId == netId);
            if (existing is not null) existing.IsActive = true;
            else _db.NetControllerPool.Add(new NetControllerPool { NetId = netId, NetControllerId = nc.Id, IsActive = true });
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Controller {nc.Callsign} updated.";
        return RedirectToAction("Index");
    }
}
