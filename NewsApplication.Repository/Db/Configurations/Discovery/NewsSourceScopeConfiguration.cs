using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Repository.Db.Configurations.Discovery;

public sealed class NewsSourceScopeConfiguration : IEntityTypeConfiguration<NewsSourceScope>
{
    public void Configure(EntityTypeBuilder<NewsSourceScope> b)
    {
        b.ToTable("NewsSourceScopes");

        // Surrogate key. The natural key is (NewsSourceId, CountryIso2, CityId), but CityId is
        // nullable for country-level runs and a Postgres primary key cannot contain a nullable
        // column — so uniqueness moves to the two partial indexes below.
        b.HasKey(x => x.Id);

        b.Property(x => x.CountryIso2).HasMaxLength(2).IsRequired();
        b.Property(x => x.PollingTier).HasMaxLength(16);
        b.Property(x => x.DiscoveredAt).HasColumnType("timestamptz");
        b.Property(x => x.IsStale).HasDefaultValue(false);

        b.Property(x => x.MatchedQueries)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .Metadata.SetValueComparer(JsonbComparers.StringList);

        b.HasOne(x => x.NewsSource)
            .WithMany(s => s.Scopes)
            .HasForeignKey(x => x.NewsSourceId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Country)
            .WithMany()
            .HasForeignKey(x => x.CountryIso2)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.City)
            .WithMany()
            .HasForeignKey(x => x.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade: job rows are the provenance of every score here, and losing
        // the scores along with an old job is not a trade worth making silently. Note this
        // also blocks deleting a DiscoveryTarget while its jobs still back live scope rows.
        b.HasOne(x => x.DiscoveryJob)
            .WithMany()
            .HasForeignKey(x => x.DiscoveryJobId)
            .OnDelete(DeleteBehavior.Restrict);

        // One score per (source, location), split the same way as DiscoveryTarget. An
        // ON CONFLICT against either of these must repeat the predicate:
        //   ON CONFLICT (...) WHERE "CityId" IS NOT NULL DO UPDATE ...
        b.HasIndex(x => new { x.NewsSourceId, x.CountryIso2, x.CityId })
            .IsUnique()
            .HasFilter("\"CityId\" IS NOT NULL");

        b.HasIndex(x => new { x.NewsSourceId, x.CountryIso2 })
            .IsUnique()
            .HasFilter("\"CityId\" IS NULL");

        // The staleness sweep: rows for this location not stamped with the job just completed.
        b.HasIndex(x => new { x.CountryIso2, x.CityId, x.DiscoveryJobId });

        // The poller's per-location query: live sources for a location, by tier.
        b.HasIndex(x => new { x.CountryIso2, x.CityId, x.IsStale, x.PollingTier });
    }
}
