using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;
using NewsApplication.Tests.ArticleFetchTesting.Infrastructure;

namespace NewsApplication.Tests.ArticleFetchTesting.Integration
{
    /// <summary>
    /// End-to-end integration tests for caching flow:
    ///  - Fetch stub articles via IArticleIngestionService
    ///  - Persist through repository
    ///  - Verify retrieval via /dev/cache/page
    /// </summary>
    public class CacheFlowTests : IClassFixture<TestAppFactory>
    {
        private readonly HttpClient _client;

        public CacheFlowTests(TestAppFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Fetch_And_Cache_Page_Should_Work()
        {
            var scopeKey = "country:US|category:top";
            var page = 1;
            var pageSize = 10;

            // Call /dev/cache/fetch to trigger caching
            var fetchResponse = await _client.PostAsync(
                $"/dev/cache/fetch?scopeKey={Uri.EscapeDataString(scopeKey)}&page={page}&pageSize={pageSize}",
                null);

            fetchResponse.EnsureSuccessStatusCode();

            // Retrieve the cached page
            var getResponse = await _client.GetAsync(
                $"/dev/cache/page?scopeKey={Uri.EscapeDataString(scopeKey)}&page={page}");

            getResponse.EnsureSuccessStatusCode();

            var payload = await getResponse.Content.ReadFromJsonAsync<object>();

            Assert.NotNull(payload);
        }
    }
}
