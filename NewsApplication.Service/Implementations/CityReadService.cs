using NewsApplication.Domain.DTOs.Scopes;
using NewsApplication.Repository.Db.Interfaces;
using NewsApplication.Service.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Service.Implementations
{
    public sealed class CityReadService : ICityReadService
    {
        private readonly ICityReadRepository _repo;
        private readonly IQueryTokenizer _tokenizer;
        public CityReadService(ICityReadRepository repo, IQueryTokenizer tokenizer)
        { _repo = repo; _tokenizer = tokenizer; }

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
