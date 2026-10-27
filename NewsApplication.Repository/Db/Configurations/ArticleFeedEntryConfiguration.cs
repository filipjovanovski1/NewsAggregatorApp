using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsApplication.Domain.DomainModels;

namespace NewsApplication.Repository.Db.Configurations;

public sealed class ArticleFeedEntryConfiguration : IEntityTypeConfiguration<ArticleFeedEntry>
{
    public void Configure(EntityTypeBuilder<ArticleFeedEntry> b)
    {
        b.ToTable("ArticleFeedEntries");
        b.HasKey(x => x.Id);

        b.Property(x => x.ExternalEntryId).HasMaxLength(2048).IsRequired();
        b.Property(x => x.EntryUrl).HasMaxLength(2048).IsRequired();
        b.Property(x => x.EntryUrlHash).HasMaxLength(71).IsRequired();
        b.Property(x => x.FeedPublishedAt).HasColumnType("timestamptz");
        b.Property(x => x.FirstSeenAt).HasColumnType("timestamptz");
        b.Property(x => x.LastSeenAt).HasColumnType("timestamptz");

        b.HasOne(x => x.NewsSourceFeed)
            .WithMany(x => x.ArticleEntries)
            .HasForeignKey(x => x.NewsSourceFeedId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Article)
            .WithMany(x => x.FeedEntries)
            .HasForeignKey(x => x.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.NewsSourceFeedId, x.ExternalEntryId }).IsUnique();
        b.HasIndex(x => new { x.NewsSourceFeedId, x.EntryUrlHash }).IsUnique();
        b.HasIndex(x => x.ArticleId);
    }
}
