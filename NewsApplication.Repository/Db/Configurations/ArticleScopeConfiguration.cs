using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsApplication.Domain.Helpers;

namespace NewsApplication.Repository.Db.Configurations;


// This is the EF configuration for the ArticleScope to link it correctly in the database to it's related entites:
// Article, Kind, 
public sealed class ArticleScopeConfiguration : IEntityTypeConfiguration<ArticleScope>
{
    public void Configure(EntityTypeBuilder<ArticleScope> builder)
    {
        builder.ToTable("ArticleScopes");

        // Surrogate key (since you chose to keep it)
        builder.HasKey(e => e.Id);

        // Columns
        builder.Property(e => e.ArticleId)
               .IsRequired()
               .HasMaxLength(128); // adjust to your provider's id length

        builder.Property(e => e.Kind)
               .IsRequired()
               .HasConversion<int>(); // enum → int

        // Value Objects (if you use them) — otherwise keep as primitives
        builder.Property(e => e.CountryIso2)
               .HasMaxLength(2);

        builder.Property(e => e.OtherValue)
               .HasMaxLength(168); // normalized free-text query (tweak as you like)

        // Relationship: Article (1) — (many) ArticleScopes
        builder.HasOne(e => e.Article)
               .WithMany(a => a.Scopes)
               .HasForeignKey(e => e.ArticleId)
               .OnDelete(DeleteBehavior.Cascade);

        // ---------- Uniqueness per kind (PostgreSQL filtered unique indexes) ----------
        // IMPORTANT: enum int values must match your Kind: City=1, Country=2, Other=3

        builder.HasIndex(e => new { e.ArticleId, e.CityId })
               .IsUnique()
               .HasFilter("\"CityId\" IS NOT NULL AND \"Kind\" = 1");   

        builder.HasIndex(e => new { e.ArticleId, e.CountryIso2 })
               .IsUnique()
               .HasFilter("\"CountryIso2\" IS NOT NULL AND \"Kind\" = 2");

        builder.HasIndex(e => new { e.ArticleId, e.OtherValue })
               .IsUnique()
               .HasFilter("\"OtherValue\" IS NOT NULL AND \"Kind\" = 3");

        // ---------- Guardrail: exactly one of (CityId, CountryIso2, OtherValue) must be set ----------
        builder.ToTable(t => t.HasCheckConstraint(
                 "CK_ArticleScope_ExactlyOneKey",
                 "(CASE WHEN \"CityId\" IS NULL THEN 0 ELSE 1 END) + " +
                 "(CASE WHEN \"CountryIso2\" IS NULL THEN 0 ELSE 1 END) + " +
                 "(CASE WHEN \"OtherValue\" IS NULL THEN 0 ELSE 1 END) = 1"
             ));
    }
}
