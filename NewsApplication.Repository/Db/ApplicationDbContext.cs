using Microsoft.EntityFrameworkCore;
using NewsApplication.Domain.DomainModels;
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
        // public DbSet<Article> Articles => Set<Article>();    // (add the rest of your aggregates as needed)
        // public DbSet<Source> Sources => Set<Source>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Automatically pick up all IEntityTypeConfiguration<> classes
            // in the same assembly as this DbContext (recommended).
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

            // If your CountryConfiguration/CityConfiguration are in a different assembly,
            // use that assembly instead, e.g.:
            // modelBuilder.ApplyConfigurationsFromAssembly(typeof(CountryConfiguration).Assembly);
        }
    }
}
