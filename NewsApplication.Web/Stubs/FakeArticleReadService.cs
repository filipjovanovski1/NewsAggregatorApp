//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using NewsApplication.Service.Dtos;
//using NewsApplication.Service.Interfaces;

//namespace NewsApplication.Web.Stubs
//{
//    public interface IFakeArticleReadService
//    {
//        PagedResult<ArticleDto> GetWorld(int page, int pageSize, int? take, string? category);
//        PagedResult<ArticleDto> GetByCountry(string iso2, int page, int pageSize, int? take, string? category);
//        PagedResult<ArticleDto> GetByCity(string cityId, int page, int pageSize, int? take, string? category);
//    }

//    public sealed class FakeArticleReadService : IFakeArticleReadService, IArticleReadService
//    {
//        private static readonly string[] Sources = { "Stub Daily", "Mock Times", "Example News" };
//        private static readonly string[] Cats = { "general", "business", "tech", "sports", "science", "health", "entertainment" };

//        // One deterministic dataset per (scope,key,category)
//        private static readonly ConcurrentDictionary<string, List<ArticleDto>> _store = new();

//        // ------- Deterministic helpers -------

//        private static int StableSeed(string input)
//        {
//            // FNV-1a 32-bit
//            unchecked
//            {
//                uint hash = 2166136261;
//                foreach (var b in Encoding.UTF8.GetBytes(input))
//                {
//                    hash ^= b;
//                    hash *= 16777619;
//                }
//                return (int)hash;
//            }
//        }

//        private static string Key(string scope, string? key, string? category)
//            => $"{scope}:{(key ?? "-").ToUpperInvariant()}:{(category ?? "-").ToLowerInvariant()}";

//        private static List<ArticleDto> EnsureDataset(string scope, string? key, string? category, int minSize = 120)
//        {
//            var k = Key(scope, key, category);
//            return _store.GetOrAdd(k, _ =>
//            {
//                var seed = StableSeed(k);
//                var rnd = new Random(seed);

//                // Build a stable set of N items; we’ll page/Take from this later.
//                var count = Math.Max(minSize, 200);
//                var list = new List<ArticleDto>(count);

//                var anchor = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc); // fixed anchor for stability across runs
//                for (int i = 0; i < count; i++)
//                {
//                    var cat = category ?? Cats[rnd.Next(Cats.Length)];
//                    var id = GuidFrom(seed, i);
//                    list.Add(new ArticleDto
//                    {
//                        Id = id,
//                        Title = $"{scope} {(key ?? "").ToUpper()} Article #{i + 1} [{cat}]",
//                        Url = "https://example.com/article/" + id.ToString("N"),
//                        PublishedUtc = anchor.AddMinutes(-(seed % 20000) - i * 7), // deterministic, not 'Now'
//                        SourceName = Sources[rnd.Next(Sources.Length)],
//                        Snippet = "Stubbed article for frontend wiring.",
//                        CountryIso2 = scope == "Country" ? key : null,
//                        CityName = scope == "City" ? key : null
//                    });
//                }

//                return list;
//            });
//        }

//        private static Guid GuidFrom(int seed, int i)
//        {
//            // produce a deterministic Guid from seed+i
//            var bytes = new byte[16];
//            var x = seed + i * 2654435761; // Knuth multiplicative hash
//            for (int b = 0; b < 16; b++)
//            {
//                bytes[b] = (byte)((x >> ((b % 4) * 8)) & 0xFF);
//                x = unchecked(x * 1103515245 + 12345);
//            }
//            return new Guid(bytes);
//        }

//        private static PagedResult<ArticleDto> Page(List<ArticleDto> src, int page, int pageSize, int take)
//        {
//            // Take from the stable dataset, then page the taken window
//            var window = src.Take(Math.Max(take, pageSize)).ToList();
//            var items = window.Skip((page - 1) * pageSize).Take(pageSize).ToList();

//            return new PagedResult<ArticleDto>
//            {
//                Items = items,
//                Total = window.Count,
//                Page = page,
//                PageSize = pageSize
//            };
//        }

//        // ------- Legacy sync endpoints your UI may still call -------

//        public PagedResult<ArticleDto> GetWorld(int page, int pageSize, int? take, string? category)
//        {
//            var ds = EnsureDataset("World", null, category, 200);
//            return Page(ds, page, pageSize, take ?? 200);
//        }

//        public PagedResult<ArticleDto> GetByCountry(string iso2, int page, int pageSize, int? take, string? category)
//        {
//            var ds = EnsureDataset("Country", iso2, category, 200);
//            return Page(ds, page, pageSize, take ?? 120);
//        }

//        public PagedResult<ArticleDto> GetByCity(string cityId, int page, int pageSize, int? take, string? category)
//        {
//            var ds = EnsureDataset("City", cityId, category, 200);
//            return Page(ds, page, pageSize, take ?? 90);
//        }

//        // ------- IArticleReadService async contract -------

//        public Task<IReadOnlyList<ArticleDto>> GetTopWorldAsync(
//            int take, string? category = null, CancellationToken cancellationToken = default)
//        {
//            var ds = EnsureDataset("World", null, category, 200);
//            return Task.FromResult<IReadOnlyList<ArticleDto>>(ds.Take(take).ToList());
//        }

//        public Task<IReadOnlyList<ArticleDto>> GetByLocationAsync(
//            string countryIso2, long? cityId = null, int take = 20, string? category = null,
//            CancellationToken cancellationToken = default)
//        {
//            var scope = cityId is null ? "Country" : "City";
//            var key = cityId?.ToString() ?? countryIso2;
//            var ds = EnsureDataset(scope, key, category, 200);
//            return Task.FromResult<IReadOnlyList<ArticleDto>>(ds.Take(take).ToList());
//        }

//        public Task<ArticleDto?> GetByIdAsync(Guid articleId, CancellationToken cancellationToken = default)
//        {
//            // simple lookup across all datasets
//            foreach (var kvp in _store)
//            {
//                var hit = kvp.Value.FirstOrDefault(a => a.Id == articleId);
//                if (hit != null) return Task.FromResult<ArticleDto?>(hit);
//            }
//            return Task.FromResult<ArticleDto?>(null);
//        }
//    }
//}
