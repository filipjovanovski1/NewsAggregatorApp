using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NewsApplication.Web.Summarization;

public sealed record OllamaChatResult(string Content, string? DoneReason);

public interface IOllamaClient
{
    Task PreloadAsync(CancellationToken cancellationToken);
    Task<OllamaChatResult> SummarizeAsync(byte[] requestBody, CancellationToken cancellationToken);
}

public sealed class OllamaClient : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient _httpClient;
    private readonly AiSummarizationOptions _options;

    public OllamaClient(HttpClient httpClient, IOptions<AiSummarizationOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task PreloadAsync(CancellationToken cancellationToken)
    {
        var requestBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = _options.Model,
            prompt = string.Empty,
            keep_alive = -1,
            stream = false,
            options = new { num_ctx = _options.ContextLength }
        });
        using var content = CreateContent(requestBody);
        using var response = await _httpClient.PostAsync("api/generate", content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<OllamaChatResult> SummarizeAsync(
        byte[] requestBody, CancellationToken cancellationToken)
    {
        using var content = CreateContent(requestBody);
        using var response = await _httpClient.PostAsync("api/chat", content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Ollama returned HTTP {(int)response.StatusCode}: {responseText}");

        var payload = JsonSerializer.Deserialize<OllamaChatResponse>(responseText, JsonOptions)
            ?? throw new InvalidOperationException("Ollama returned an empty JSON response.");
        return new(payload.Message?.Content?.Trim() ?? string.Empty, payload.DoneReason);
    }

    private static ByteArrayContent CreateContent(byte[] requestBody)
    {
        var content = new ByteArrayContent(requestBody);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            "application/json; charset=utf-8");
        return content;
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; init; }
        [JsonPropertyName("done_reason")]
        public string? DoneReason { get; init; }
    }

    private sealed class OllamaMessage { public string? Content { get; init; } }
}
