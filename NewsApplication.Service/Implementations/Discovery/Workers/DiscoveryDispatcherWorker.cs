using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsApplication.Repository.Db.Interfaces.Discovery;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Service.Interfaces.Discovery;

namespace NewsApplication.Service.Implementations.Discovery.Workers;

public sealed class DiscoveryDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DiscoverySchedulerOptions _options;
    private readonly ILogger<DiscoveryDispatcherWorker> _logger;

    public DiscoveryDispatcherWorker(
        IServiceScopeFactory scopes,
        IOptions<DiscoverySchedulerOptions> options,
        ILogger<DiscoveryDispatcherWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        await RunOnce(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnce(stoppingToken);
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IDiscoveryPipelineClient>();
            var health = await pipeline.GetHealthAsync(ct);
            if (health?.Accepting == false)
                return;

            var targets = scope.ServiceProvider.GetRequiredService<IDiscoveryTargetRepository>();
            var jobs = scope.ServiceProvider.GetRequiredService<IDiscoveryJobService>();
            var due = await targets.GetDueAsync(
                DateTimeOffset.UtcNow, Math.Max(1, _options.DispatchBatchSize), ct);

            foreach (var target in due)
            {
                var result = await jobs.StartAsync(target, ct);
                if (result.Outcome is DiscoveryStartOutcome.RateLimited or
                    DiscoveryStartOutcome.Unavailable)
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Discovery dispatch cycle failed");
        }
    }
}
