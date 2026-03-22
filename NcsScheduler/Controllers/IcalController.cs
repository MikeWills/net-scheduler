using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NcsScheduler.Data;
using NcsScheduler.Models.Domain;
using System.Text;

namespace NcsScheduler.Controllers;

/// <summary>
/// Serves a personalized iCalendar (.ics) feed for each net controller.
/// The feed URL is authenticated by a per-controller opaque token so it
/// can be subscribed to by any calendar app without requiring a login.
/// </summary>
public class IcalController : Controller
{
    private readonly ApplicationDbContext _db;

    private static readonly TimeZoneInfo EasternZone = FindEasternZone();

    private static TimeZoneInfo FindEasternZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Eastern Standard Time", out var tz)) return tz;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/New_York", out tz)) return tz;
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Converts a UTC date + UTC time to the Eastern local date.
    /// Standing assignments store DayOfWeek in Eastern time, so we need this
    /// to match correctly (e.g. Sunday 03:00z = Saturday Eastern).
    /// </summary>
    private static DateOnly ToEasternDate(DateOnly utcDate, TimeOnly utcTime)
    {
        var utcDt = utcDate.ToDateTime(utcTime, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcDt, EasternZone));
    }

    public IcalController(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// GET /Ical/Feed/{token}
    /// Returns an iCalendar feed of the NCS's upcoming sessions.
    /// </summary>
    [HttpGet]
    [Route("Ical/Feed/{token}")]
    public async Task<IActionResult> Feed(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return NotFound();

        var nc = await _db.NetControllers
            .FirstOrDefaultAsync(c => c.IcalToken == token);

        if (nc is null)
            return NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowEnd = today.AddDays(90);

        // ── Load data ─────────────────────────────────────────────────────────

        var standingAssignments = await _db.StandingAssignments
            .Include(sa => sa.Net)
            .Where(sa =>
                sa.NetControllerId == nc.Id &&
                (sa.EffectiveTo == null || sa.EffectiveTo >= today))
            .ToListAsync();

        // Explicit sub/regular assignments (not volunteer) in the window
        var explicitAssignments = await _db.SessionAssignments
            .Include(a => a.NetSession).ThenInclude(s => s.Net)
            .Where(a =>
                a.NetControllerId == nc.Id &&
                a.AssignmentType != AssignmentType.Volunteer &&
                a.Status != AssignmentStatus.Cancelled &&
                a.NetSession.SessionDate >= today &&
                a.NetSession.SessionDate <= windowEnd)
            .ToListAsync();

        var unavailabilities = await _db.Unavailabilities
            .Where(u =>
                u.NetControllerId == nc.Id &&
                u.EndDate >= today &&
                u.StartDate <= windowEnd)
            .ToListAsync();

        // Pre-load existing sessions for nets with standing assignments so we
        // can detect whether a different NCS has been confirmed as sub
        var standingNetIds = standingAssignments.Select(sa => sa.NetId).Distinct().ToList();
        var existingSessions = await _db.NetSessions
            .Include(s => s.Assignments.Where(a =>
                a.Status == AssignmentStatus.Confirmed ||
                a.Status == AssignmentStatus.Scheduled))
            .Where(s =>
                standingNetIds.Contains(s.NetId) &&
                s.SessionDate >= today &&
                s.SessionDate <= windowEnd)
            .ToListAsync();

        // ── Build event list ──────────────────────────────────────────────────

        var events = new List<(DateOnly Date, TimeOnly TimeUtc, int NetId, string NetName,
            string? FrequencyMhz, string? FrequencyRange, bool IsSubstitute)>();
        var coveredKeys = new HashSet<(int NetId, DateOnly Date)>();

        // 1. Derive sessions from standing assignments
        // StandingAssignment.DayOfWeek is stored in Eastern local time,
        // so we convert each UTC date to Eastern to check the match.
        foreach (var sa in standingAssignments)
        {
            var net = sa.Net!;
            for (var d = today; d <= windowEnd; d = d.AddDays(1))
            {
                var easternDate = ToEasternDate(d, net.ScheduledTimeUtc);
                if (easternDate.DayOfWeek != sa.DayOfWeek) continue;
                if (sa.EffectiveFrom > d) continue;
                if (!net.IsInSeasonForDate(d)) continue;

                // Skip dates the controller has marked unavailable
                bool unavailable = unavailabilities.Any(u =>
                    u.StartDate <= d && u.EndDate >= d &&
                    (u.NetId == null || u.NetId == sa.NetId));
                if (unavailable) continue;

                // Skip dates where a different NCS has been confirmed as sub
                var session = existingSessions
                    .FirstOrDefault(s => s.NetId == sa.NetId && s.SessionDate == d);
                if (session is not null)
                {
                    bool subbedOut = session.Assignments.Any(a =>
                        a.AssignmentType == AssignmentType.Substitute &&
                        a.NetControllerId != nc.Id);
                    if (subbedOut) continue;
                }

                events.Add((d, net.ScheduledTimeUtc, net.Id, net.Name,
                    net.FrequencyMhz, net.FrequencyRange, false));
                coveredKeys.Add((sa.NetId, d));
            }
        }

        // 2. Explicit sub/regular assignments not already covered by standing
        foreach (var a in explicitAssignments)
        {
            var key = (a.NetSession.NetId, a.NetSession.SessionDate);
            if (coveredKeys.Contains(key)) continue;

            events.Add((
                a.NetSession.SessionDate,
                a.NetSession.ScheduledTimeUtc,
                a.NetSession.NetId,
                a.NetSession.Net!.Name,
                a.NetSession.Net.FrequencyMhz,
                a.NetSession.Net.FrequencyRange,
                a.AssignmentType == AssignmentType.Substitute
            ));
        }

        events = events
            .OrderBy(e => e.Date)
            .ThenBy(e => e.TimeUtc)
            .ToList();

        // ── Generate iCalendar content ────────────────────────────────────────

        var sb = new StringBuilder();
        // RFC 5545 requires CRLF line endings
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//NCS Scheduler//NCS Schedule//EN\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:PUBLISH\r\n");
        sb.Append($"X-WR-CALNAME:NCS Schedule - {nc.Callsign}\r\n");
        sb.Append("X-WR-CALDESC:Your upcoming net control sessions\r\n");
        sb.Append("X-WR-TIMEZONE:UTC\r\n");
        sb.Append("REFRESH-INTERVAL;VALUE=DURATION:PT6H\r\n");
        sb.Append("X-PUBLISHED-TTL:PT6H\r\n");

        foreach (var ev in events)
        {
            // Net start time in UTC
            var netStart = new DateTime(ev.Date.Year, ev.Date.Month, ev.Date.Day,
                ev.TimeUtc.Hour, ev.TimeUtc.Minute, 0, DateTimeKind.Utc);
            // Event starts 30 minutes early for check-in
            var dtStart = netStart.AddMinutes(-30);
            var dtEnd = netStart.AddHours(1);

            // UID must be stable across refreshes — keyed on net + date
            var uid = $"{ev.Date:yyyyMMdd}-net{ev.NetId}-{nc.Id}@ncsscheduler";

            var timeLabel = $"{ev.TimeUtc:HH:mm}z";
            var summary = ev.IsSubstitute
                ? $"NCS (Sub): {ev.NetName} at {timeLabel}"
                : $"NCS: {ev.NetName} at {timeLabel}";

            var descParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ev.FrequencyMhz))
            {
                var freq = ev.FrequencyMhz;
                if (!string.IsNullOrWhiteSpace(ev.FrequencyRange))
                    freq += $" ({ev.FrequencyRange})";
                descParts.Add($"Frequency: {freq}");
            }
            descParts.Add($"Net starts at {timeLabel} — early check-in opens 30 min prior.");
            descParts.Add(ev.IsSubstitute
                ? "You are scheduled as substitute Net Control Station."
                : "You are scheduled as Net Control Station.");
            var description = string.Join("\\n", descParts);

            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:{uid}\r\n");
            sb.Append($"DTSTART:{dtStart:yyyyMMddTHHmmssZ}\r\n");
            sb.Append($"DTEND:{dtEnd:yyyyMMddTHHmmssZ}\r\n");
            sb.Append(FoldLine($"SUMMARY:{summary}"));
            sb.Append(FoldLine($"DESCRIPTION:{description}"));
            sb.Append("STATUS:CONFIRMED\r\n");
            sb.Append("TRANSP:OPAQUE\r\n");
            sb.Append("BEGIN:VALARM\r\n");
            sb.Append("TRIGGER:-PT15M\r\n");
            sb.Append("ACTION:DISPLAY\r\n");
            sb.Append("DESCRIPTION:NCS check-in starts in 15 minutes\r\n");
            sb.Append("END:VALARM\r\n");
            sb.Append("END:VEVENT\r\n");
        }

        sb.Append("END:VCALENDAR\r\n");

        return Content(sb.ToString(), "text/calendar; charset=utf-8");
    }

    /// <summary>
    /// Folds a long iCal property line at 75 octets per RFC 5545 §3.1.
    /// Continuation lines begin with a single space.
    /// </summary>
    private static string FoldLine(string line)
    {
        if (line.Length <= 75)
            return line + "\r\n";

        var sb = new StringBuilder();
        // First chunk: up to 75 chars
        sb.Append(line[..75]);
        sb.Append("\r\n");
        var pos = 75;
        while (pos < line.Length)
        {
            // Subsequent chunks: space + up to 74 chars
            var len = Math.Min(74, line.Length - pos);
            sb.Append(' ');
            sb.Append(line.AsSpan(pos, len));
            sb.Append("\r\n");
            pos += len;
        }
        return sb.ToString();
    }
}
