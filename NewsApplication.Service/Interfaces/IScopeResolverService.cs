using NewsApplication.Domain.DTOs.Scopes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewsApplication.Service.Interfaces
{
    public interface IScopeResolverService
    {
        Task<ScopePreviewDTO> PreviewAsync(string query, CancellationToken ct);
    }
}
