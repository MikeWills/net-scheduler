using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Helpers;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;
using NcsScheduler.Services;

namespace NcsScheduler.Controllers;

[Authorize(Policy = "SuperAdminOnly")]
public class NetsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IScheduleService _scheduleService;

    public NetsController(ApplicationDbContext db, IScheduleService scheduleService)
    {
        _db = db;
        _scheduleService = scheduleService;
    }

    public async Task<IActionResult> Index()
    {
        var nets = (await _db.Nets
            .Include(n => n.ScheduleRules.Where(r => r.IsActive))
            .Include(n => n.CoordinatorAssignments.Where(nca => nca.EndDate == null))
                .ThenInclude(nca => nca.BandCoordinator).ThenInclude(bc => bc.NetController)
            .ToListAsync())
            .OrderBy(n => BandHelper.SortKey(n.Band))
            .ThenBy(n => n.ScheduledTimeUtc)
            .ThenBy(n => n.Name)
            .ToList();
        return View(nets);
    }

    [HttpGet]
    public IActionResult Create() => View(new NetEditViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(NetEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var net = new Net
        {
            Name = model.Name,
            Band = model.Band,
            FrequencyMhz = model.FrequencyMhz,
            FrequencyRange = model.FrequencyRange,
            Description = model.Description,
            ScheduledTimeUtc = model.ScheduledTimeUtc,
            IsActive = true,
            SeasonStart = model.SeasonStart,
            SeasonEnd   = model.SeasonEnd
        };

        foreach (var dow in model.SelectedDays)
            net.ScheduleRules.Add(new NetScheduleRule { DayOfWeek = dow, IsActive = true });

        _db.Nets.Add(net);
        await _db.SaveChangesAsync();

        // Generate sessions immediately so the new net appears on the schedule
        // without requiring an app restart.
        await _scheduleService.GenerateSessionsAsync(net.Id);

        TempData["Success"] = $"Net '{net.Name}' created.";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var net = await _db.Nets
            .Include(n => n.ScheduleRules.Where(r => r.IsActive))
            .FirstOrDefaultAsync(n => n.Id == id);
        if (net is null) return NotFound();

        var vm = new NetEditViewModel
        {
            Id = net.Id,
            Name = net.Name,
            Band = net.Band,
            FrequencyMhz = net.FrequencyMhz,
            FrequencyRange = net.FrequencyRange,
            Description = net.Description,
            ScheduledTimeUtc = net.ScheduledTimeUtc,
            IsActive = net.IsActive,
            SeasonStart = net.SeasonStart,
            SeasonEnd   = net.SeasonEnd,
            SelectedDays = net.ScheduleRules.Select(r => r.DayOfWeek).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(NetEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var net = await _db.Nets
            .Include(n => n.ScheduleRules)
            .FirstOrDefaultAsync(n => n.Id == model.Id);
        if (net is null) return NotFound();

        net.Name = model.Name;
        net.Band = model.Band;
        net.FrequencyMhz = model.FrequencyMhz;
        net.FrequencyRange = model.FrequencyRange;
        net.Description = model.Description;
        net.ScheduledTimeUtc = model.ScheduledTimeUtc;
        net.IsActive = model.IsActive;
        net.SeasonStart = model.SeasonStart;
        net.SeasonEnd   = model.SeasonEnd;

        // Replace schedule rules
        foreach (var rule in net.ScheduleRules) rule.IsActive = false;
        foreach (var dow in model.SelectedDays)
        {
            var existing = net.ScheduleRules.FirstOrDefault(r => r.DayOfWeek == dow);
            if (existing is not null) existing.IsActive = true;
            else net.ScheduleRules.Add(new NetScheduleRule { DayOfWeek = dow, IsActive = true });
        }

        await _db.SaveChangesAsync();

        // Regenerate sessions in case the schedule days or UTC time changed.
        await _scheduleService.GenerateSessionsAsync(net.Id);

        TempData["Success"] = "Net updated.";
        return RedirectToAction("Index");
    }

}
