using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewsApplication.Domain.DTOs.Discovery;
using NewsApplication.Repository.Db.Interfaces.Discovery;
using NewsApplication.Service.Interfaces.Client;

namespace NewsApplication.Service.Implementations.Discovery.Workers;

public sealed class FeedRevalidationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DiscoverySchedulerOptions _options;
    private readonly ILogger<FeedRevalidationWorker> _logger;

    public FeedRevalidationWorker(
        IServiceScopeFactory scopes,
        IOptions<DiscoverySchedulerOptions> options,
        ILogger<FeedRevalidationWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnce(stoppingToken);
    }

    private async Task RunOnce(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<INewsSourceRepository>();
            var pipeline = scope.ServiceProvider.GetRequiredService<IDiscoveryPipelineClient>();
            var batchSize = Math.Clamp(_options.FeedBatchSize, 1, 500);
            var feeds = await repository.GetFeedsForValidationAsync(int.MaxValue, ct);
            if (feeds.Count == 0)
                return;

            foreach (var batch in feeds.Chunk(batchSize))
            {
                var request = batch.Select(x => new FeedValidationRequestDTO
                {
                    SourceFeedId = x.Id,
                    Url = x.Url,
                    Domain = x.NewsSource?.Domain
                }).ToList();

                var results = await pipeline.ValidateFeedsAsync(request, ct);
                await repository.UpdateValidatedFeedsAsync(results, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Nightly feed validation failed");
        }
    }
}
