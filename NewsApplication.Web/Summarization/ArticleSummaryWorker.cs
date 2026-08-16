using Microsoft.Extensions.Options;

namespace NewsApplication.Web.Summarization;

public sealed class ArticleSummaryWorker : BackgroundService
{
    private readonly ArticleSummaryCoordinator _coordinator;
    private readonly IOllamaBodyBuilder _bodyBuilder;
    private readonly IOllamaClient _ollamaClient;
    private readonly AiSummarizationOptions _options;
    private readonly ILogger<ArticleSummaryWorker> _logger;

    public ArticleSummaryWorker(
        ArticleSummaryCoordinator coordinator,
        IOllamaBodyBuilder bodyBuilder,
        IOllamaClient ollamaClient,
        IOptions<AiSummarizationOptions> options,
        ILogger<ArticleSummaryWorker> logger)
    {
        _coordinator = coordinator;
        _bodyBuilder = bodyBuilder;
        _ollamaClient = ollamaClient;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Local Ollama summarization is disabled.");
            return;
        }

        try
        {
            await _ollamaClient.PreloadAsync(stoppingToken);
            _logger.LogInformation(
                "Preloaded Ollama model {Model} with context {ContextLength}.",
                _options.Model, _options.ContextLength);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Ollama preload failed. Summary jobs will continue trying the local server.");
        }

        await foreach (var job in _coordinator.Reader.ReadAllAsync(stoppingToken))
        {
            try { await ProcessJobAsync(job, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _coordinator.MarkFailed(job);
                _logger.LogWarning(ex,
                    "Summary generation failed for article {ArticleId} in {Language}.",
                    job.ArticleId, job.Language);
            }
        }
    }

    private async Task ProcessJobAsync(SummaryJob job, CancellationToken cancellationToken)
    {
        var body = await _bodyBuilder.BuildAsync(job, null, cancellationToken);
        var result = await _ollamaClient.SummarizeAsync(body, cancellationToken);
        if (!TranslatedArticleParser.TryParse(
                result.Content, result.DoneReason, out var translatedArticle))
        {
            var retryBody = await _bodyBuilder.BuildAsync(
                job, result.Content, cancellationToken);
            result = await _ollamaClient.SummarizeAsync(retryBody, cancellationToken);
        }

        if (!TranslatedArticleParser.TryParse(
                result.Content, result.DoneReason, out translatedArticle))
        {
            _coordinator.MarkFailed(job);
            _logger.LogWarning(
                "Ollama returned an invalid translated article twice for article {ArticleId}.",
                job.ArticleId);
            return;
        }
        _coordinator.MarkReady(job, translatedArticle!.Title, translatedArticle.Summary);
    }
}
