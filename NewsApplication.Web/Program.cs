using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db;
using NewsApplication.Repository.Db.Implementations;
using NewsApplication.Repository.Db.Importers;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Implementations;
using NewsApplication.Service.Implementations.Client;
using NewsApplication.Service.Implementations.Ingestion;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Service.Interfaces.Ingestion;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(opts =>
{
    opts.AddPolicy("Client", p => p
        .WithOrigins("http://localhost:5173", "https://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var connStr = builder.Configuration.GetConnectionString("Default")!;

// Build a data source with dynamic JSON enabled
var dsb = new NpgsqlDataSourceBuilder(connStr);
dsb.EnableDynamicJson();                  // <-- Npgsql 8 setting
var dataSource = dsb.Build();

var migrationsAssembly = typeof(ApplicationDbContext).Assembly.FullName;
// Services
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
    opt.UseNpgsql(dataSource, npg => npg.MigrationsAssembly(migrationsAssembly)),
    contextLifetime: ServiceLifetime.Scoped,
    optionsLifetime: ServiceLifetime.Singleton);

builder.Services.AddDbContextFactory<ApplicationDbContext>(opt =>
    opt.UseNpgsql(dataSource, npg => npg.MigrationsAssembly(migrationsAssembly)));

builder.Services.AddScoped<CountryImporter>();
builder.Services.AddScoped<CityImporter>();

// Repositories (EF Core / data access)
builder.Services.AddScoped<ICityReadRepository, CityReadRepository>();
builder.Services.AddScoped<ICountryReadRepository, CountryReadRepository>();
builder.Services.AddScoped<IArticleRepository, ArticleRepository>();

// Application services (orchestration)
builder.Services.AddScoped<ICityReadService, CityReadService>();
builder.Services.AddScoped<ICountryReadService, CountryReadService>();
builder.Services.AddSingleton<IQueryTokenizer, QueryTokenizer>();

// Scope Preview
builder.Services.AddScoped<IScopePolicy, ScopePolicy>();
builder.Services.AddScoped<IScopeResolverService, ScopeResolverService>();

builder.Services.Configure<NewsdataOptions>(builder.Configuration.GetSection("Newsdata"));
builder.Services.AddHttpClient<INewsdataClient, NewsdataClient>((sp, http) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsdataOptions>>().Value;
    http.BaseAddress = new Uri(opt.BaseUrl); // full endpoint
});
builder.Services.AddHttpClient("nominatim", http =>
{
    http.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    http.DefaultRequestHeaders.UserAgent.ParseAdd("NewsAggregatorApp/1.0 (+https://newsaggregatorapp.local/contact)");
});
builder.Services.AddScoped<IArticleIngestionService, ArticleIngestionService>();

builder.Services.AddControllers(); 
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("Client");

// dev: auto-apply migrations (keep your block)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}
// add before app.Run();
app.MapGet("/_routes", (IEnumerable<EndpointDataSource> sources) =>
{
    var routes = sources
        .SelectMany(s => s.Endpoints)
        .OfType<RouteEndpoint>()
        .Select(e => new
        {
            Pattern = e.RoutePattern.RawText,
            Display = e.DisplayName
        });
    return Results.Ok(routes);
});

app.MapControllers();
app.Run();

