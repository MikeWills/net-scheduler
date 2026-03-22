using System.Text;
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

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpGet]
    public IActionResult Import() => View();

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        var csv = "Callsign,Name,Email,Phone,MemberNumber\r\n" +
                  "W1ABC,Jane Smith,jane@example.com,555-1234,12345\r\n" +
                  "K9XYZ,John Doe,,555-9876,\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        return File(bytes, "text/csv", "ncs-import-template.csv");
    }

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("", "Please select a CSV file.");
            return View();
        }

        string content;
        using (var reader = new System.IO.StreamReader(file.OpenReadStream()))
            content = await reader.ReadToEndAsync();

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            ModelState.AddModelError("", "The file contains no data rows.");
            return View();
        }

        var existingCallsigns = (await _db.NetControllers
            .Select(nc => nc.Callsign)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var vm = new ImportPreviewViewModel();
        var seenCallsigns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Skip header row
        foreach (var rawLine in lines.Skip(1))
        {
            var line = rawLine.Trim('\r', ' ');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = SplitCsvLine(line);
            var row = new ImportRowViewModel
            {
                Callsign    = cols.Length > 0 ? cols[0].Trim().ToUpper() : "",
                Name        = cols.Length > 1 ? cols[1].Trim() : "",
                Email       = cols.Length > 2 ? NullIfEmpty(cols[2]) : null,
                Phone       = cols.Length > 3 ? NullIfEmpty(cols[3]) : null,
                MemberNumber = cols.Length > 4 ? NullIfEmpty(cols[4]) : null,
            };

            var error = ValidateRow(row, existingCallsigns, seenCallsigns);
            if (error is not null)
                vm.InvalidRows.Add(new InvalidImportRow { Row = row, Error = error });
            else
            {
                seenCallsigns.Add(row.Callsign);
                vm.ValidRows.Add(row);
            }
        }

        if (vm.ValidRows.Count == 0 && vm.InvalidRows.Count == 0)
        {
            ModelState.AddModelError("", "No data rows found in the file.");
            return View();
        }

        return View("ImportPreview", vm);
    }

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmImport(List<ImportRowViewModel> rows)
    {
        if (rows.Count == 0)
        {
            TempData["Error"] = "No rows to import.";
            return RedirectToAction("Import");
        }

        var existingCallsigns = (await _db.NetControllers
            .Select(nc => nc.Callsign)
            .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int imported = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Callsign) || existingCallsigns.Contains(row.Callsign))
                continue;

            _db.NetControllers.Add(new NetController
            {
                Callsign     = row.Callsign.ToUpper(),
                Name         = row.Name,
                Email        = row.Email,
                Phone        = row.Phone,
                MemberNumber = row.MemberNumber,
                IsActive     = true
            });
            existingCallsigns.Add(row.Callsign);
            imported++;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"{imported} controller{(imported == 1 ? "" : "s")} imported successfully.";
        return RedirectToAction("Index");
    }

    private static string? ValidateRow(ImportRowViewModel row, HashSet<string> existingCallsigns, HashSet<string> seenCallsigns)
    {
        if (string.IsNullOrWhiteSpace(row.Callsign))
            return "Callsign is required.";
        if (string.IsNullOrWhiteSpace(row.Name))
            return "Name is required.";
        if (existingCallsigns.Contains(row.Callsign))
            return $"Callsign {row.Callsign} already exists in the database.";
        if (seenCallsigns.Contains(row.Callsign))
            return $"Callsign {row.Callsign} appears more than once in this file.";
        if (!string.IsNullOrEmpty(row.Email))
        {
            try { _ = new System.Net.Mail.MailAddress(row.Email); }
            catch { return "Email address is not valid."; }
        }
        return null;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return [.. fields];
    }

    private static string? NullIfEmpty(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
