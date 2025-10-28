using NewsApplication.Domain.DomainModels;
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Interfaces.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NewsApplication.Tests.ArticleFetchTesting.Doubles
{
    /// <summary>
    /// A stubbed version of the Newsdata API client.
    /// Returns hard-coded sample articles to avoid hitting the real API.
    /// </summary>
    public sealed class StubNewsdataClient : INewsdataClient
    {
        private const string ProviderName = "NEWSDATA";

        public Task<(List<Article>, string?)> FetchPageAsync(
            string scopeKey, string? pageToken, int pageSize, CancellationToken ct)
        {
            var list = new List<Article>
            {
                new Article
                {
                    ArticleId = "stub-1",
                    Provider = ProviderName,
                    Title = "Stub Article #1",
                    Description = "This is a stub article for integration testing.",
                    ImageUrl = "https://example.com/image1.jpg",
                    Publisher = "StubPublisher",
                    Url = "https://example.com/1",
                    PublishedTime = DateTime.UtcNow.AddHours(-3),
                    Categories = new() { "top", "testing" }
                },
                new Article
                {
                    ArticleId = "stub-2",
                    Provider = ProviderName,
                    Title = "Stub Article #2",
                    Description = "Another stubbed article entry.",
                    ImageUrl = "https://example.com/image2.jpg",
                    Publisher = "StubPublisher",
                    Url = "https://example.com/2",
                    PublishedTime = DateTime.UtcNow.AddHours(-6),
                    Categories = new() { "science", "testing" }
                }
            };

            // Return up to pageSize articles and no nextPageToken
            return Task.FromResult((list.GetRange(0, Math.Min(list.Count, pageSize)), (string?)null));
        }
    }
}
