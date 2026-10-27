using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Domain.DomainModels;

public sealed class ArticleFeedEntry : BaseEntity
{
    public Guid NewsSourceFeedId { get; set; }
    public NewsSourceFeed? NewsSourceFeed { get; set; }

    public Guid ArticleId { get; set; }
    public Article? Article { get; set; }

    public required string ExternalEntryId { get; set; }
    public required string EntryUrl { get; set; }
    public required string EntryUrlHash { get; set; }
    public DateTimeOffset? FeedPublishedAt { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
