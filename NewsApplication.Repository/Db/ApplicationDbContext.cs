using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NewsApplication.Domain.Cache;
using NewsApplication.Domain.DomainModels;
using NewsApplication.Domain.DomainModels.Discovery;
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

        // Discovery pipeline. Configured by IEntityTypeConfiguration classes under
        // Configurations/Discovery, which the ApplyConfigurationsFromAssembly call at the end
        // of OnModelCreating picks up — nothing to add there.
        public DbSet<DiscoveryTarget> DiscoveryTargets => Set<DiscoveryTarget>();
        public DbSet<DiscoveryJob> DiscoveryJobs => Set<DiscoveryJob>();
        public DbSet<NewsSource> NewsSources => Set<NewsSource>();
        public DbSet<NewsSourceFeed> NewsSourceFeeds => Set<NewsSourceFeed>();
        public DbSet<NewsSourceScope> NewsSourceScopes => Set<NewsSourceScope>();


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

                b.HasKey(a => a.Id);

                b.Property(a => a.Id)
                    .ValueGeneratedNever();

                b.Property(a => a.Provider)
                    .IsRequired();

                b.Property(a => a.ProviderArticleId);

                b.HasIndex(a => new
                    {
                        a.Provider,
                        a.ProviderArticleId
                    })
                    .IsUnique()
                    .HasFilter("\"ProviderArticleId\" IS NOT NULL");

                b.Property(a => a.Categories)
                    .HasColumnType("jsonb")
                    .HasDefaultValueSql("'[]'::jsonb")
                    .Metadata.SetValueComparer(categoriesComparer);

                b.Property(a => a.InsertedAt)
                    .HasColumnType("timestamptz")
                    .HasDefaultValueSql("now()");
            });
            // ArticleCacheItem
            modelBuilder.Entity<ArticleCache>(b =>
            {
                b.ToTable("ArticleCaches");
                b.HasKey(x => x.Id);

                b.Property(x => x.ExpiresAt)
                .HasColumnType("timestamptz");   // <- explicit
                b.HasIndex(x => x.ExpiresAt);

                // Enforce single live row per (ScopeKey, Page)
                b.HasIndex(x => new { x.ScopeKey, x.Page }).IsUnique();
            });

            modelBuilder.Entity<ArticleCacheItem>(b =>
            {
                b.ToTable("ArticleCacheItems");

                b.HasKey(x => new
                {
                    x.ArticleCacheId,
                    x.ArticleId
                });

                b.HasOne(x => x.ArticleCache)
                    .WithMany(c => c.Items)
                    .HasForeignKey(x => x.ArticleCacheId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Article)
                    .WithMany()
                    .HasForeignKey(x => x.ArticleId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasIndex(x => x.ArticleId);
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
