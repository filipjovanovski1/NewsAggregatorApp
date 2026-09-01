using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsApplication.Domain.DomainModels.Discovery;
using NewsApplication.Repository.Db.Interfaces.Discovery;

namespace NewsApplication.Service.Implementations.Discovery.Workers;

public sealed class StaleDiscoveryJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DiscoverySchedulerOptions _options;
    private readonly ILogger<StaleDiscoveryJobWorker> _logger;

    public StaleDiscoveryJobWorker(
        IServiceScopeFactory scopes,
        IOptions<DiscoverySchedulerOptions> options,
        ILogger<StaleDiscoveryJobWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnce(stoppingToken);
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<IDiscoveryJobRepository>();
            var now = DateTimeOffset.UtcNow;
            var stale = await jobs.GetStaleQueuedAsync(now.AddHours(-6), ct);

            foreach (var job in stale)
            {
                job.Status = DiscoveryJobStatus.Stale;
                job.CompletedAt = now;
                job.ErrorStage = "callback";
                job.ErrorType = "timeout";
                job.ErrorMessage = "No callback received within the six-hour queued horizon.";

                var target = job.DiscoveryTarget;
                if (target is null)
                    continue;
                target.ConsecutiveFailures++;
                target.NextDueAt = now.AddHours(
                    Math.Min(Math.Pow(2, target.ConsecutiveFailures), 24 * 7));
                if (target.ConsecutiveFailures >= 5)
                    target.IsEnabled = false;
            }

            await jobs.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Stale discovery-job sweep failed");
        }
    }
}
