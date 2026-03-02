namespace NcsScheduler.Services;

/// <summary>
/// Application-level settings bound from the "App" section of appsettings.json.
/// In production, override with environment variable App__BaseUrl=https://yourdomain.com
/// </summary>
public class AppSettings
{
    /// <summary>
    /// The public base URL of this application (e.g. "https://ncs.example.com").
    /// Used when generating absolute URLs such as iCal feed links.
    /// Leave blank in development — the app will fall back to the incoming request's host.
    /// </summary>
    public string BaseUrl { get; set; } = "";
}
