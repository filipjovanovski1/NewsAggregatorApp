using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Repository.Db.Configurations.Discovery;

public sealed class NewsSourceFeedConfiguration : IEntityTypeConfiguration<NewsSourceFeed>
{
    public void Configure(EntityTypeBuilder<NewsSourceFeed> b)
    {
        b.ToTable("NewsSourceFeeds");
        b.HasKey(x => x.Id);

        // Capped rather than left as unbounded text because it is indexed below, and a btree
        // entry cannot exceed ~2704 bytes. Real feed URLs are nowhere near this.
        b.Property(x => x.Url).HasMaxLength(1024).IsRequired();
        b.Property(x => x.Title).HasMaxLength(512);
        b.Property(x => x.Language).HasMaxLength(16);
        b.Property(x => x.LastEtag).HasMaxLength(256);

        b.Property(x => x.LatestEntry).HasColumnType("timestamptz");
        b.Property(x => x.LastPolledAt).HasColumnType("timestamptz");
        b.Property(x => x.IsActive).HasDefaultValue(true);

        // What the §5.3 upsert conflicts on. Its DO UPDATE list must cover the discovery and
        // validation fields only: LastPolledAt and LastEtag are poller state and are never in
        // it, or every quarterly discovery run replays each feed's whole backlog.
        b.HasIndex(x => new { x.NewsSourceId, x.Url }).IsUnique();

        b.HasOne(x => x.NewsSource)
            .WithMany(s => s.Feeds)
            .HasForeignKey(x => x.NewsSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // The poller's due-feeds query.
        b.HasIndex(x => new { x.IsActive, x.LastPolledAt });
    }
}
