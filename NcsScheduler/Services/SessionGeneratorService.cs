namespace NcsScheduler.Services;

/// <summary>
/// Background service that generates NetSessions for the rolling 9-week window.
/// Runs once immediately on startup, then every 24 hours.
/// Uses IServiceScopeFactory to create a scoped DbContext (can't inject scoped into singleton).
/// </summary>
public class SessionGeneratorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SessionGeneratorService> _logger;

    public SessionGeneratorService(IServiceScopeFactory scopeFactory, ILogger<SessionGeneratorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait briefly for the app to fully start before first run
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        await RunGenerationAsync();

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunGenerationAsync();
    }

    private async Task RunGenerationAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduleService = scope.ServiceProvider.GetRequiredService<IScheduleService>();
            await scheduleService.GenerateAllSessionsAsync(weeksAhead: 9);
            _logger.LogInformation("Session generation completed at {Time}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session generation failed");
        }
    }
}
