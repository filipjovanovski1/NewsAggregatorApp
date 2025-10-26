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

    }
}
