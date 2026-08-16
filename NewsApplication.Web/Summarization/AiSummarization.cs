using Microsoft.Extensions.Options;
using NewsApplication.Domain.DomainModels;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace NewsApplication.Web.Summarization;

public sealed class AiSummarizationOptions
{
    public const string SectionName = "AiSummarization";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = string.Empty;
    public int ContextLength { get; set; }
    public int OutputTokenLimit { get; set; }
    public double? Temperature { get; set; }
    public int? Seed { get; set; }
    public string Think { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = "article-translation-v4";
    public int QueueCapacity { get; set; } = 512;
    public int RequestTimeoutSeconds { get; set; } = 120;
    public string PowerShellExecutable { get; set; } = "powershell.exe";
    public string ScriptPath { get; set; } = "Scripts/Ollama/New-OllamaArticleBody.ps1";
    public string BridgeScriptPath { get; set; } = "Scripts/Ollama/OllamaBodyBuilderBridge.ps1";
}

public static class SummaryLanguage
{
    private static readonly IReadOnlyDictionary<string, string> Supported =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = "zh-CN", ["es"] = "es", ["en"] = "en",
            ["hi"] = "hi", ["pt"] = "pt", ["bn"] = "bn",
            ["ru"] = "ru", ["ja"] = "ja", ["tr"] = "tr",
            ["vi"] = "vi", ["ar"] = "ar", ["ko"] = "ko",
            ["id"] = "id", ["de"] = "de", ["fr"] = "fr",
            ["mk"] = "mk"
        };

    public static IReadOnlyCollection<string> Codes => Supported.Values.ToArray();

    public static bool TryNormalize(string? value, out string normalized)
    {
        var candidate = (value ?? string.Empty).Trim().Replace('_', '-');
        return Supported.TryGetValue(candidate, out normalized!);
    }
}

public static class SummaryStatus
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

public sealed record ArticleSummarySnapshot(
    string ArticleId, string Language, string Status,
    string? TranslatedTitle, string? Summary);

public sealed record SummaryJob(
    string Key, string ArticleId, string Title, string Description,
    string Publisher, string Language);

public interface IArticleSummaryCoordinator
{
    ArticleSummarySnapshot GetOrQueue(Article article, string language);
}

public sealed class ArticleSummaryCoordinator : IArticleSummaryCoordinator
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, ArticleSummarySnapshot> _entries = new();
    private readonly Channel<SummaryJob> _jobs;
    private readonly AiSummarizationOptions _options;

    public ArticleSummaryCoordinator(IOptions<AiSummarizationOptions> options)
    {
        _options = options.Value;
        _jobs = Channel.CreateBounded<SummaryJob>(new BoundedChannelOptions(
            Math.Max(1, _options.QueueCapacity))
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    internal ChannelReader<SummaryJob> Reader => _jobs.Reader;

    public ArticleSummarySnapshot GetOrQueue(Article article, string language)
    {
        if (!SummaryLanguage.TryNormalize(language, out var normalizedLanguage))
            throw new ArgumentException("Unsupported summary language.", nameof(language));

        if (!_options.Enabled)
            return new(article.ArticleId, normalizedLanguage, SummaryStatus.Failed, null, null);

        var key = CreateKey(article, normalizedLanguage);
        if (_entries.TryGetValue(key, out var existing)) return existing;

        var pending = new ArticleSummarySnapshot(
            article.ArticleId, normalizedLanguage, SummaryStatus.Pending, null, null);
        if (!_entries.TryAdd(key, pending)) return _entries[key];

        var job = new SummaryJob(
            key, article.ArticleId, article.Title ?? string.Empty,
            article.Description ?? string.Empty, article.Publisher ?? string.Empty,
            normalizedLanguage);

        if (!_jobs.Writer.TryWrite(job))
        {
            var failed = pending with { Status = SummaryStatus.Failed };
            _entries[key] = failed;
            return failed;
        }

        return pending;
    }

    internal void MarkReady(SummaryJob job, string translatedTitle, string summary) =>
        _entries[job.Key] = new(
            job.ArticleId, job.Language, SummaryStatus.Ready, translatedTitle, summary);

    internal void MarkFailed(SummaryJob job) =>
        _entries[job.Key] = new(
            job.ArticleId, job.Language, SummaryStatus.Failed, null, null);

    private string CreateKey(Article article, string language)
    {
        var input = string.Join('\n', article.ArticleId, language, _options.Model,
            _options.PromptVersion, NormalizeContent(article.Title),
            NormalizeContent(article.Description));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"{article.ArticleId}:{Convert.ToHexString(hash)}";
    }

    private static string NormalizeContent(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty);
        return WhitespaceRegex.Replace(HtmlTagRegex.Replace(decoded, " "), " ").Trim();
    }
}

public sealed record TranslatedArticle(string Title, string Summary);

public static class TranslatedArticleParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(
        string? content,
        string? doneReason,
        out TranslatedArticle? translatedArticle)
    {
        translatedArticle = null;
        if (string.IsNullOrWhiteSpace(content) ||
            string.Equals(doneReason, "length", StringComparison.OrdinalIgnoreCase))
            return false;

        var candidate = RemoveCodeFence(content.Trim());
        TranslationPayload? payload;
        try { payload = JsonSerializer.Deserialize<TranslationPayload>(candidate, JsonOptions); }
        catch (JsonException) { return false; }

        var title = (payload?.Title ?? payload?.TranslatedTitle ?? string.Empty).Trim();
        var summary = (payload?.Summary ?? string.Empty).Trim();
        if (title.Length is 0 or > 500 || SummaryValidator.NeedsRetry(summary, doneReason))
            return false;

        translatedArticle = new(title, summary);
        return true;
    }

    private static string RemoveCodeFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal)) return value;
        var firstLineEnd = value.IndexOf('\n');
        var closingFence = value.LastIndexOf("```", StringComparison.Ordinal);
        if (firstLineEnd < 0 || closingFence <= firstLineEnd) return value;
        return value[(firstLineEnd + 1)..closingFence].Trim();
    }

    private sealed class TranslationPayload
    {
        public string? Title { get; init; }
        public string? TranslatedTitle { get; init; }
        public string? Summary { get; init; }
    }
}

public static class SummaryValidator
{
    private static readonly HashSet<char> SentenceTerminators =
        ['.', '!', '?', '?', '?', '?', '?', '?'];
    private static readonly char[] ClosingPunctuation =
        ['"', '\'', '?', '?', '?', ')', ']'];

    public static bool NeedsRetry(string? summary, string? doneReason)
    {
        if (string.IsNullOrWhiteSpace(summary) ||
            string.Equals(doneReason, "length", StringComparison.OrdinalIgnoreCase))
            return true;

        var candidate = summary.Trim();
        if (candidate.Length > 350) return true;

        candidate = candidate.TrimEnd(ClosingPunctuation);
        return candidate.Length == 0 || !SentenceTerminators.Contains(candidate[^1]);
    }
}
