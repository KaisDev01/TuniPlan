using BL.Managers;

namespace TuniPlan.Infrastructure;

/// <summary>Background job: appointment reminders, review requests and expiry of unanswered requests (every 5 minutes).</summary>
public sealed class ReminderWorker(IServiceScopeFactory scopeFactory, ILogger<ReminderWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Jobs:RemindersEnabled", true)) return;
        var interval = TimeSpan.FromMinutes(configuration.GetValue("Jobs:ReminderIntervalMinutes", 5));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IReminderManager>().ProcessAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reminder job failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
