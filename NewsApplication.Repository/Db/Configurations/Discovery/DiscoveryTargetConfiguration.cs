using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Repository.Db.Configurations.Discovery;

public sealed class DiscoveryTargetConfiguration : IEntityTypeConfiguration<DiscoveryTarget>
{
    public void Configure(EntityTypeBuilder<DiscoveryTarget> b)
    {
        b.ToTable("DiscoveryTargets");
        b.HasKey(x => x.Id);

        b.Property(x => x.CountryIso2).HasMaxLength(2).IsRequired();
        b.Property(x => x.CadenceDays).HasDefaultValue(90);
        b.Property(x => x.IsEnabled).HasDefaultValue(true);

        b.Property(x => x.NextDueAt).HasColumnType("timestamptz");
        b.Property(x => x.LastSuccessAt).HasColumnType("timestamptz");

        b.HasOne(x => x.Country)
            .WithMany()
            .HasForeignKey(x => x.CountryIso2)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.City)
            .WithMany()
            .HasForeignKey(x => x.CityId)
            .OnDelete(DeleteBehavior.Restrict);

        // One target per location. Postgres will not enforce this with a single unique index,
        // because NULL is never equal to NULL there — (MK, null) could be inserted a hundred
        // times. Two partial indexes split the nullable and non-nullable cases; the quoting
        // matches the filter already on Country.Iso3.
        b.HasIndex(x => new { x.CountryIso2, x.CityId })
            .IsUnique()
            .HasFilter("\"CityId\" IS NOT NULL");

        b.HasIndex(x => x.CountryIso2)
            .IsUnique()
            .HasFilter("\"CityId\" IS NULL");

        // The dispatcher's only query: enabled targets that are due, best first.
        b.HasIndex(x => new { x.IsEnabled, x.NextDueAt, x.Priority });
    }
}
