using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NewsApplication.Domain.DomainModels;

namespace NewsApplication.Service.Interfaces.Client;

public interface INewsdataClient
{
    Task<(List<Article> articles, string? nextPageToken)> FetchPageAsync(
        string scopeKey, string? pageToken, int pageSize, CancellationToken ct);
}
