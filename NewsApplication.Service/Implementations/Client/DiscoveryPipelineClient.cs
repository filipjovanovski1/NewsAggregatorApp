using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NewsApplication.Domain.DTOs.Discovery;
using NewsApplication.Service.Interfaces.Client;

namespace NewsApplication.Service.Implementations.Client;

public sealed class DiscoveryPipelineClient : IDiscoveryPipelineClient
{
    private readonly HttpClient _http;
    private readonly ILogger<DiscoveryPipelineClient> _logger;

    public DiscoveryPipelineClient(HttpClient http, ILogger<DiscoveryPipelineClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<DiscoveryStartResult> StartJobAsync(
        StartDiscoveryJobRequestDTO request,
        CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(
            "jobs", request, DiscoveryJsonOptions.SnakeCase, ct);

        var retryAfter = ReadRetryAfter(response);
        var body = await response.Content.ReadAsStringAsync(ct);

        return response.StatusCode switch
        {
            HttpStatusCode.Accepted => new DiscoveryStartResult(
                DiscoveryStartOutcome.Accepted,
                QueuePosition: ReadQueuePosition(body)),
            HttpStatusCode.BadRequest => new DiscoveryStartResult(
                DiscoveryStartOutcome.InvalidRequest, Error: body),
            HttpStatusCode.Conflict => new DiscoveryStartResult(
                DiscoveryStartOutcome.Conflict, Error: body),
            HttpStatusCode.TooManyRequests => new DiscoveryStartResult(
                DiscoveryStartOutcome.RateLimited, retryAfter, Error: body),
            HttpStatusCode.ServiceUnavailable => new DiscoveryStartResult(
                DiscoveryStartOutcome.Unavailable, retryAfter, Error: body),
            _ => throw new HttpRequestException(
                $"Discovery pipeline returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode)
        };
    }

    public async Task<PipelineHealthDTO?> GetHealthAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync("health", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PipelineHealthDTO>(
            DiscoveryJsonOptions.SnakeCase, ct);
    }

    public async Task<IReadOnlyList<FeedValidationResultDTO>> ValidateFeedsAsync(
        IReadOnlyList<FeedValidationRequestDTO> feeds,
        CancellationToken ct)
    {
        if (feeds.Count is < 1 or > 500)
            throw new ArgumentOutOfRangeException(
                nameof(feeds), "Feed validation batches must contain 1-500 rows.");

        using var response = await _http.PostAsJsonAsync(
            "feeds/validate",
            new FeedValidationBatchRequestDTO { Feeds = feeds.ToList() },
            DiscoveryJsonOptions.SnakeCase,
            ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<FeedValidationResponseDTO>(
            DiscoveryJsonOptions.SnakeCase, ct)
            ?? throw new JsonException("Feed validation returned an empty response.");

        if (payload.Results.Count != feeds.Count)
            throw new InvalidOperationException(
                "Feed validation did not return exactly one result per requested feed.");

        _logger.LogInformation("Validated {Count} discovery feeds", feeds.Count);
        return payload.Results;
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta)
            return delta;
        if (retry?.Date is { } date)
            return date - DateTimeOffset.UtcNow > TimeSpan.Zero
                ? date - DateTimeOffset.UtcNow
                : TimeSpan.Zero;
        return null;
    }

    private static int? ReadQueuePosition(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("queue_position", out var value) &&
                   value.TryGetInt32(out var position)
                ? position
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
