using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace NewsApplication.Service.Implementations
{
    public sealed class CityReadService : ICityReadService
    {
        private readonly ICityReadRepository _repo;
        private readonly IQueryTokenizer _tokenizer;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CityReadService> _logger;

        private static readonly SemaphoreSlim NominatimLock = new(1, 1);
        private static DateTime _nextNominatimAllowedUtc = DateTime.UtcNow;
        public CityReadService(ICityReadRepository repo, IQueryTokenizer tokenizer, IHttpClientFactory httpClientFactory, ILogger<CityReadService> logger)
        { _repo = repo; _tokenizer = tokenizer; _httpClientFactory = httpClientFactory; _logger = logger; }

        public async Task<IReadOnlyList<GeoCandidateDTO>> SearchAsync(string query, int limit, CancellationToken ct)
        {
            var norm = _tokenizer.Normalize(query);
            if (string.IsNullOrWhiteSpace(norm)) return Array.Empty<GeoCandidateDTO>();

            var capped = Math.Clamp(limit, 1, 100);
            return await _repo.SearchAsync(norm, capped, ct);
        }

        public Task<GeoCandidateDTO?> GetByIdAsync(Guid id, CancellationToken ct)
            => _repo.GetByIdAsync(id, ct);

        public async Task<GeoCandidateDTO?> FindNearestAsync(double lat, double lng, double maxDistanceKm, CancellationToken ct)
        {
            var candidates = await _repo.FindNearestAsync(lat, lng, limit: 8, ct);

            GeoCandidateDTO? best = null;
            var bestDistance = double.MaxValue;

            foreach (var candidate in candidates)
            {
                if (candidate.Lat is null || candidate.Lng is null) continue;

                var dist = HaversineKm(lat, lng, candidate.Lat.Value, candidate.Lng.Value);
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    best = candidate;
                }
            }

            if (best is null) return null;
            return bestDistance <= maxDistanceKm ? best : null;
        }

        public async Task<string?> EnsureLocalNameAsync(GeoCandidateDTO city, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(city.LocalName)) return city.LocalName;
            if (city.Lat is null || city.Lng is null) return null;
            if (!Guid.TryParse(city.Id, out var cityId)) return null;

            var persisted = await _repo.GetByIdAsync(cityId, ct);
            if (!string.IsNullOrWhiteSpace(persisted?.LocalName))
            {
                return persisted!.LocalName;
            }

            var localName = await FetchLocalNameAsync(city.Lat.Value, city.Lng.Value, ct);
            if (string.IsNullOrWhiteSpace(localName)) return null;
            localName = localName.Trim();

            try
            {
                return await _repo.SetLocalNameAsync(cityId, localName!, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist LocalName for City {CityId}", cityId);
                return localName;
            }
        }

        private async Task<string?> FetchLocalNameAsync(double lat, double lng, CancellationToken ct)
        {
            await NominatimLock.WaitAsync(ct);
            try
            {
                var wait = _nextNominatimAllowedUtc - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }
                _nextNominatimAllowedUtc = DateTime.UtcNow.AddSeconds(1);
            }
            finally
            {
                NominatimLock.Release();
            }

            var client = _httpClientFactory.CreateClient("nominatim");
            var url =
                $"reverse?format=jsonv2&lat={lat.ToString(CultureInfo.InvariantCulture)}&lon={lng.ToString(CultureInfo.InvariantCulture)}&zoom=10&addressdetails=1&namedetails=1";
            using var resp = await client.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nominatim returned {StatusCode} for {Lat},{Lng}", resp.StatusCode, lat, lng);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            string? val = null;
            var root = doc.RootElement;
            val ??= TryGetString(root, "localname");
            val ??= TryGetString(root, "name");

            if (root.TryGetProperty("namedetails", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                val ??= TryGetString(details, "name");
                if (val is null)
                {
                    foreach (var prop in details.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            val = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(val)) break;
                        }
                    }
                }
            }

            val ??= TryGetString(root, "display_name")?.Split(',').FirstOrDefault();

            return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
        }

        private static string? TryGetString(JsonElement element, string name)
            => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;

        private static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371d;
            static double ToRad(double deg) => deg * Math.PI / 180d;

            var dLat = ToRad(lat2 - lat1);
            var dLng = ToRad(lng2 - lng1);

            var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                    Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

    }
}
