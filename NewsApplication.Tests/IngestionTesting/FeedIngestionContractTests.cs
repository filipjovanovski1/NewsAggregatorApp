using System.Text.Json;
using NewsApplication.Domain.DTOs.Ingestion;

namespace NewsApplication.Tests.IngestionTesting;

public sealed class FeedIngestionContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Python_Completion_Payload_Binds_To_Backend_Dto()
    {
        const string json = """
            {
              "leaseVersion": 42,
              "feed": {
                "requestedUrl": "https://example.mk/feed/",
                "finalUrl": "https://example.mk/rss.xml",
                "httpStatus": 200,
                "etag": "\"v18\"",
                "lastModified": "Thu, 22 Oct 2026 09:10:00 GMT",
                "polledAt": "2026-10-23T12:01:12Z"
              },
              "articles": [{
                "externalEntryId": "story-123",
                "entryUrl": "https://example.mk/story/123",
                "canonicalUrl": "https://example.mk/story/123",
                "title": "Example story",
                "description": "Summary",
                "contentText": "Extracted text",
                "author": "Author",
                "language": "mk",
                "imageUrl": null,
                "publishedAt": "2026-10-23T10:31:00Z",
                "modifiedAt": null,
                "categories": ["local"],
                "contentHash": "sha256:abc",
                "extractionStatus": "extracted",
                "extractor": "trafilatura",
                "extractorVersion": "2.3.1"
              }]
            }
            """;

        var result = JsonSerializer.Deserialize<CompleteFeedPollDTO>(json, Json);

        Assert.NotNull(result);
        Assert.Equal(42, result!.LeaseVersion);
        Assert.Equal(200, result.Feed.HttpStatus);
        var article = Assert.Single(result.Articles);
        Assert.Equal("story-123", article.ExternalEntryId);
        Assert.Equal("Extracted text", article.ContentText);
        Assert.Equal("extracted", article.ExtractionStatus);
    }

    [Fact]
    public void Claim_Response_Serializes_With_Camel_Case_Names()
    {
        var leaseId = Guid.NewGuid();
        var response = new FeedClaimResponseDTO([
            new FeedClaimDTO(
                leaseId,
                7,
                new DateTimeOffset(2026, 10, 23, 12, 5, 0, TimeSpan.Zero),
                Guid.NewGuid(),
                "https://example.mk/feed.xml",
                Guid.NewGuid(),
                "example.mk",
                "Example",
                "mk",
                "\"v1\"",
                null,
                null,
                "high")
        ]);

        var json = JsonSerializer.Serialize(response, Json);

        Assert.Contains("\"leaseId\"", json);
        Assert.Contains("\"leaseVersion\":7", json);
        Assert.Contains("\"feedUrl\":\"https://example.mk/feed.xml\"", json);
        Assert.DoesNotContain("LeaseId", json);
    }
}
