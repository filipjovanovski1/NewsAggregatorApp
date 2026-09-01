using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsApplication.Domain.DomainModels.Discovery;

namespace NewsApplication.Repository.Db.Configurations.Discovery;

public sealed class DiscoveryJobConfiguration : IEntityTypeConfiguration<DiscoveryJob>
{
    public void Configure(EntityTypeBuilder<DiscoveryJob> b)
    {
        b.ToTable("DiscoveryJobs");
        b.HasKey(x => x.Id);

        // Minted in DiscoveryJobService and sent to the pipeline as job_id, so EF must not
        // substitute one of its own.
        b.Property(x => x.Id).ValueGeneratedNever();

        // Stored as text, not the underlying int: these rows are read by hand during the
        // bootstrap, and "3" in a status column answers no question anyone is asking.
        b.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        b.Property(x => x.StartedAt).HasColumnType("timestamptz");
        b.Property(x => x.CompletedAt).HasColumnType("timestamptz");

        b.Property(x => x.ErrorStage).HasMaxLength(64);
        b.Property(x => x.ErrorType).HasMaxLength(128);

        // Written whole on the callback and never mutated in place, so the default reference
        // comparer is enough here — unlike Warnings below, which is a mutable collection.
        b.Property(x => x.Stats).HasColumnType("jsonb");

        b.Property(x => x.Warnings)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .Metadata.SetValueComparer(JsonbComparers.StringList);

        b.HasOne(x => x.DiscoveryTarget)
            .WithMany(t => t.Jobs)
            .HasForeignKey(x => x.DiscoveryTargetId)
            .OnDelete(DeleteBehavior.Cascade);

        // The stale sweep: jobs still Queued past the long horizon.
        b.HasIndex(x => new { x.Status, x.StartedAt });

        b.HasIndex(x => x.DiscoveryTargetId);
    }
}
