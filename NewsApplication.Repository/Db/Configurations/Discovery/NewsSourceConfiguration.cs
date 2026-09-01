using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Repository.Db.Configurations.Discovery;

public sealed class NewsSourceConfiguration : IEntityTypeConfiguration<NewsSource>
{
    public void Configure(EntityTypeBuilder<NewsSource> b)
    {
        b.ToTable("NewsSources");
        b.HasKey(x => x.Id);

        // 253 is the DNS name limit. The uniqueness is what the §5.3 upsert conflicts on, and
        // it only means one row per site because import normalizes subdomains away first —
        // Postgres would happily store mia.mk and new.mia.mk as two distinct sites.
        b.Property(x => x.Domain).HasMaxLength(253).IsRequired();
        b.HasIndex(x => x.Domain).IsUnique();

        b.Property(x => x.Url).HasMaxLength(1024);
        b.Property(x => x.Language).HasMaxLength(16);
        b.Property(x => x.Classification).HasMaxLength(32);

        b.Property(x => x.Categories)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .Metadata.SetValueComparer(JsonbComparers.StringList);

        b.Property(x => x.FirstDiscoveredAt).HasColumnType("timestamptz");
        b.Property(x => x.LastDiscoveredAt).HasColumnType("timestamptz");
        b.Property(x => x.IsActive).HasDefaultValue(true);

        // The poller's filter: active NEWS_SOURCE rows only, never DISCOVERY_SOURCE.
        b.HasIndex(x => new { x.IsActive, x.Classification });
    }
}
