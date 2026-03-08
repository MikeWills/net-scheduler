using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using NcsScheduler.Models.ViewModels;

namespace NcsScheduler.Services;

public class ScheduleService : IScheduleService
{
    private readonly ApplicationDbContext _db;

    public ScheduleService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ── Eastern-time helpers ─────────────────────────────────────────────────
    // Sessions are stored as UTC dates + UTC times. To decide which local
    // calendar day a session actually falls on we convert to Eastern Time,
    // which handles both EST (UTC-5) and EDT (UTC-4) automatically.

    private static readonly TimeZoneInfo EasternZone = FindEasternZone();

    private static TimeZoneInfo FindEasternZone()
    {
        // Windows uses "Eastern Standard Time"; Linux/macOS use "America/New_York"
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Eastern Standard Time", out var tz)) return tz;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out tz)) return tz;
        return TimeZoneInfo.Utc;
    }

    private static DateOnly ToEasternDate(DateOnly utcDate, TimeOnly utcTime)
    {
        var utcDt = utcDate.ToDateTime(utcTime, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcDt, EasternZone));
    }

    private static TimeOnly ToEasternTime(DateOnly utcDate, TimeOnly utcTime)
    {
        var utcDt = utcDate.ToDateTime(utcTime, DateTimeKind.Utc);
        return TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcDt, EasternZone));
    }

    public async Task GenerateAllSessionsAsync(int weeksAhead = 8)
    {
        var nets = await _db.Nets
            .Where(n => n.IsActive)
            .Select(n => n.Id)
            .ToListAsync();

        foreach (var netId in nets)
            await GenerateSessionsAsync(netId, weeksAhead);
    }

    public async Task GenerateSessionsAsync(int netId, int weeksAhead = 8)
    {
        var net = await _db.Nets
            .Include(n => n.ScheduleRules.Where(r => r.IsActive))
            .FirstOrDefaultAsync(n => n.Id == netId && n.IsActive);

        if (net is null) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var end = today.AddDays(weeksAhead * 7);

        // Generate sessions from schedule rules
        var activeDays = net.ScheduleRules.Select(r => r.DayOfWeek).ToHashSet();
        for (var date = today; date <= end; date = date.AddDays(1))
        {
            if (!activeDays.Contains(date.DayOfWeek)) continue;
            if (!net.IsInSeasonForDate(date)) continue;
            await EnsureSessionExistsAsync(net, date);
        }

        await _db.SaveChangesAsync();
    }

    private async Task EnsureSessionExistsAsync(Net net, DateOnly date)
    {
        var exists = await _db.NetSessions
            .AnyAsync(s => s.NetId == net.Id && s.SessionDate == date);

        if (!exists)
        {
            _db.NetSessions.Add(new NetSession
            {
                NetId = net.Id,
                SessionDate = date,
                ScheduledTimeUtc = net.ScheduledTimeUtc
            });
        }
    }

    public async Task<SlotResolution> ResolveSlotAsync(int netSessionId)
    {
        var session = await _db.NetSessions
            .Include(s => s.Assignments)
                .ThenInclude(a => a.NetController)
            .FirstOrDefaultAsync(s => s.Id == netSessionId);

        if (session is null)
            return new SlotResolution(null, null, true, false);

        // 1. Check for an explicit non-cancelled session assignment
        var explicit_ = session.Assignments
            .Where(a => a.Status != AssignmentStatus.Cancelled)
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefault();

        if (explicit_ is not null)
        {
            bool isConfirmed = explicit_.Status == AssignmentStatus.Confirmed;
            return new SlotResolution(
                explicit_.NetController,
                explicit_.AssignmentType,
                NeedsNcs: false,
                HasVolunteer: explicit_.AssignmentType == AssignmentType.Volunteer && !isConfirmed
            );
        }

        // 2. Fall back to standing assignment.
        // Convert the UTC session date+time to Eastern local date so that a controller
        // stored as "Sunday" is resolved for a session at 03:00z Monday (11 PM Sunday ET)
        // rather than being matched against the UTC Monday date.
        var localDate = ToEasternDate(session.SessionDate, session.ScheduledTimeUtc);

        var standing = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa =>
                sa.NetId == session.NetId &&
                sa.DayOfWeek == localDate.DayOfWeek &&
                sa.EffectiveFrom <= session.SessionDate &&
                (sa.EffectiveTo == null || sa.EffectiveTo >= session.SessionDate))
            .FirstOrDefaultAsync();

        if (standing is null)
            return new SlotResolution(null, null, true, false);

        // 3. Check unavailability for this controller on this date
        var unavailable = await _db.Unavailabilities.AnyAsync(u =>
            u.NetControllerId == standing.NetControllerId &&
            u.StartDate <= session.SessionDate && u.EndDate >= session.SessionDate &&
            (u.NetId == null || u.NetId == session.NetId));

        if (unavailable || session.IsForcedOpen)
        {
            var hasVolunteer = session.Assignments.Any(a =>
                a.AssignmentType == AssignmentType.Volunteer &&
                a.Status != AssignmentStatus.Cancelled);
            return new SlotResolution(standing.NetController, AssignmentType.Regular, true, hasVolunteer);
        }

        return new SlotResolution(standing.NetController, AssignmentType.Regular, false, false);
    }

    public async Task<ScheduleViewModel> GetPublicScheduleAsync(DateOnly from, DateOnly to)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var nets = await _db.Nets
            .Where(n => n.IsActive)
            .ToListAsync();

        // Hide nets outside their configured season window, then sort by Eastern local time
        nets = nets
            .Where(n => n.IsInSeasonForDate(today))
            .OrderBy(n => ToEasternTime(today, n.ScheduledTimeUtc))
            .ToList();
        var inSeasonNetIds = nets.Select(n => n.Id).ToHashSet();

        var sessions = await _db.NetSessions
            .Include(s => s.Assignments.Where(a => a.Status != AssignmentStatus.Cancelled))
                .ThenInclude(a => a.NetController)
            .Where(s => inSeasonNetIds.Contains(s.NetId) && s.SessionDate >= from && s.SessionDate <= to)
            .ToListAsync();

        var unavailabilities = await _db.Unavailabilities
            .Where(u => u.StartDate <= to && u.EndDate >= from)
            .ToListAsync();

        var standingAssignments = await _db.StandingAssignments
            .Include(sa => sa.NetController)
            .Where(sa => sa.EffectiveTo == null || sa.EffectiveTo >= from)
            .ToListAsync();

        var vm = new ScheduleViewModel { From = from, To = to };

        // Build week rows (Sun–Sat)
        var current = from;
        // Align to Sunday
        while (current.DayOfWeek != DayOfWeek.Sunday)
            current = current.AddDays(-1);

        while (current <= to)
        {
            var weekEnd = current.AddDays(6);
            var row = new WeekRow { WeekStart = current, WeekEnd = weekEnd };

            var weekSessions = sessions.Where(s => s.SessionDate >= current && s.SessionDate <= weekEnd);
            foreach (var session in weekSessions)
            {
                var slot = ResolveSlotFromLoaded(session, unavailabilities, standingAssignments);
                row.Slots[session.Id] = slot;
            }

            vm.Weeks.Add(row);
            current = current.AddDays(7);
        }

        // Build net columns
        foreach (var net in nets)
        {
            var col = new NetColumn
            {
                NetId = net.Id,
                NetName = net.Name,
                Band = net.Band,
                ScheduledTimeUtc = net.ScheduledTimeUtc,
                FrequencyMhz = net.FrequencyMhz,
                FrequencyRange = net.FrequencyRange
            };
            vm.Nets.Add(col);
        }

        return vm;
    }

    private ScheduleSlot ResolveSlotFromLoaded(
        NetSession session,
        List<Unavailability> allUnavailabilities,
        List<StandingAssignment> allStanding)
    {
        var slot = new ScheduleSlot
        {
            SessionId = session.Id,
            NetId = session.NetId,
            SessionDate = session.SessionDate
        };

        // Check for explicit assignment
        var explicit_ = session.Assignments
            .Where(a => a.Status != AssignmentStatus.Cancelled)
            .OrderByDescending(a => a.AssignedAt)
            .FirstOrDefault();

        if (explicit_ is not null)
        {
            slot.Callsign = explicit_.NetController.Callsign;
            slot.MemberNumber = explicit_.NetController.MemberNumber;
            slot.AssignmentType = explicit_.AssignmentType;
            bool isVolunteerPending = explicit_.AssignmentType == AssignmentType.Volunteer
                && explicit_.Status == AssignmentStatus.Scheduled;
            slot.HasVolunteer = isVolunteerPending;
            slot.NeedsNcs = isVolunteerPending;
            return slot;
        }

        // Fall back to standing assignment.
        // Convert the UTC session date+time to Eastern local date so that a controller
        // stored as "Sunday" is resolved for a session at 03:00z Monday (11 PM Sunday ET).
        // Using Eastern TimeZoneInfo handles both EST and EDT automatically, correctly
        // leaving 05:00z sessions on the same local calendar day (1 AM Eastern).
        var localDate = ToEasternDate(session.SessionDate, session.ScheduledTimeUtc);

        var standing = allStanding
            .Where(sa =>
                sa.NetId == session.NetId &&
                sa.DayOfWeek == localDate.DayOfWeek &&
                sa.EffectiveFrom <= session.SessionDate &&
                (sa.EffectiveTo == null || sa.EffectiveTo >= session.SessionDate))
            .FirstOrDefault();

        if (standing is null)
        {
            slot.NeedsNcs = true;
            return slot;
        }

        var unavailable = allUnavailabilities.Any(u =>
            u.NetControllerId == standing.NetControllerId &&
            u.StartDate <= session.SessionDate && u.EndDate >= session.SessionDate &&
            (u.NetId == null || u.NetId == session.NetId));

        if (unavailable || session.IsForcedOpen)
        {
            slot.NeedsNcs = true;
            // Still show the standing controller's callsign so the coordinator knows who's out
            slot.Callsign = standing.NetController.Callsign;
            slot.MemberNumber = standing.NetController.MemberNumber;
            return slot;
        }

        slot.Callsign = standing.NetController.Callsign;
        slot.MemberNumber = standing.NetController.MemberNumber;
        slot.AssignmentType = AssignmentType.Regular;
        return slot;
    }
}
