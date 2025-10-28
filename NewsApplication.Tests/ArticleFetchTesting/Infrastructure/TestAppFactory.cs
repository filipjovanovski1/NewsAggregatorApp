using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using NewsApplication.Tests.ArticleFetchTesting.Doubles;
using System.Linq;

namespace NewsApplication.Tests.ArticleFetchTesting.Infrastructure
{
    /// <summary>
    /// Spins up the full application in a test host for integration testing.
    /// Replaces the live INewsdataClient with a stubbed one.
    /// </summary>
    public sealed class TestAppFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureServices(services =>
            {
                // Remove any existing registration for INewsdataClient
                var existing = services.SingleOrDefault(s => s.ServiceType == typeof(INewsdataClient));
                if (existing is not null)
                    services.Remove(existing);

                // Replace with stub
                services.AddSingleton<INewsdataClient, StubNewsdataClient>();

                // (Optional) Override DbContext to point to a test Postgres
                // or in-memory substitute, if you want DB isolation in tests.
            });

            return base.CreateHost(builder);
        }
    }
}
