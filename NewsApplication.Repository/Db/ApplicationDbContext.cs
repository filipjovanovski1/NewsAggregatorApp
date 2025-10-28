using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NewsApplication.Domain.Cache;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Repository.Db.Configurations.ScopeHelpers;
// If your IEntityTypeConfiguration<T> classes live in a separate assembly/namespace,
// add: using NewsApplication.Repository.Configurations;

namespace NewsApplication.Repository.Db
{
    public sealed class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // --- DbSets ---
        public DbSet<Country> Countries => Set<Country>();
        public DbSet<City> Cities => Set<City>();
        public DbSet<Article> Articles => Set<Article>();
        public DbSet<ArticleCache> ArticleCaches => Set<ArticleCache>();
        public DbSet<ArticleCacheItem> ArticleCacheItems => Set<ArticleCacheItem>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<CitySearchRow>().HasNoKey();     // we pass SQL at call time
            modelBuilder.Entity<CountrySearchRow>().HasNoKey();  // just a query shape
            modelBuilder.Entity<Country>(b =>
            {
                // Optional: if you prefer fluent instead of attribute
                // b.Property(x => x.Iso3).HasMaxLength(3);

                // PostgreSQL filtered unique index (ignore NULLs)
                b.HasIndex(x => x.Iso3)
                 .IsUnique()
                 .HasFilter("\"Iso3\" IS NOT NULL"); // <-- Postgres quoting
            });
            // -- Article.Categories -> jsonb with reliable change tracking
            var categoriesComparer = new ValueComparer<List<string>>(
                (a, b) =>
                    a != null && b != null &&
                    a.Count == b.Count &&
                    a.SequenceEqual(b, StringComparer.Ordinal),
                a => a.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())),
                a => a.ToList()
            );
            modelBuilder.Entity<Article>(b =>
            {
                b.ToTable("Articles");
                b.Property(x => x.Categories)
                 .HasColumnType("jsonb")    // Postgres jsonb
                  .HasDefaultValueSql("'[]'::jsonb")
                 .Metadata.SetValueComparer(categoriesComparer);

                b.Property<DateTimeOffset>("InsertedAt")
                .HasDefaultValueSql("NOW() AT TIME ZONE 'UTC'");
            });
            // ArticleCacheItem
            modelBuilder.Entity<ArticleCache>(b =>
            {
                b.ToTable("ArticleCaches");
                b.HasKey(x => x.Id);
                b.HasIndex(x => new { x.ScopeKey, x.Page }).IsUnique();
                b.HasIndex(x => x.ExpiresAt);
            });

            modelBuilder.Entity<ArticleCacheItem>(b =>
            {
                b.ToTable("ArticleCacheItems");
                // Composite PK: natural idempotency for (page, article)
                b.HasKey(x => new { x.ArticleCacheId, x.ArticleId });

                b.HasOne(x => x.ArticleCache).WithMany(c => c.Items)
                    .HasForeignKey(x => x.ArticleCacheId)
                    .OnDelete(DeleteBehavior.Cascade);   // delete links when page cache expires

                b.HasOne(x => x.Article).WithMany()
                    .HasForeignKey(x => x.ArticleId)
                    .OnDelete(DeleteBehavior.Restrict);  // prevent deleting an Article still referenced

                b.HasIndex(x => x.ArticleId);            // speeds up orphan-article GC
            });


            // Automatically pick up all IEntityTypeConfiguration<> classes
            // in the same assembly as this DbContext (recommended).
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            // If your CountryConfiguration/CityConfiguration are in a different assembly,
            // use that assembly instead, e.g.:
            // modelBuilder.ApplyConfigurationsFromAssembly(typeof(CountryConfiguration).Assembly);
        }
    }
}
