namespace NcsScheduler.Helpers;

public static class BandHelper
{
    /// <summary>
    /// Numeric sort key derived from a band name by extracting its leading digits.
    /// Examples: "10m" → 10, "80m" → 80, "160m" → 160.
    /// Bands with no numeric prefix (or null/empty) sort last (int.MaxValue).
    /// Use this wherever nets or band filter buttons need to appear in
    /// natural amateur-radio wavelength order (shortest → longest).
    /// </summary>
    public static int SortKey(string? band)
    {
        if (string.IsNullOrWhiteSpace(band)) return int.MaxValue;
        var m = System.Text.RegularExpressions.Regex.Match(band, @"(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : int.MaxValue;
    }
}
