using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NewsApplication.Domain.DTOs.Discovery;
using NewsApplication.Service.Implementations.Client;
using NewsApplication.Service.Interfaces.Discovery;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("api/discovery")]
public sealed class DiscoveryController : ControllerBase
{
    private readonly IDiscoveryResultImportService _imports;
    private readonly DiscoveryPipelineOptions _options;

    public DiscoveryController(
        IDiscoveryResultImportService imports,
        IOptions<DiscoveryPipelineOptions> options)
    {
        _imports = imports;
        _options = options.Value;
    }

    [HttpPost("jobs/{jobId:guid}/result")]
    public async Task<IActionResult> Result(Guid jobId, CancellationToken ct)
    {
        if (!HasValidBearerToken())
            return Unauthorized();

        DiscoveryResultDTO? result;
        try
        {
            result = await JsonSerializer.DeserializeAsync<DiscoveryResultDTO>(
                Request.Body, DiscoveryJsonOptions.SnakeCase, ct);
        }
        catch (JsonException exception)
        {
            return BadRequest(new { error = "invalid_json", exception.Message });
        }

        if (result is null || result.SchemaVersion != 1)
            return BadRequest(new { error = "unsupported_schema_version" });

        if (!Guid.TryParse(result.JobId, out var payloadJobId) || payloadJobId != jobId)
            return BadRequest(new { error = "job_id_mismatch" });

        try
        {
            var outcome = await _imports.ImportAsync(jobId, result, ct);
            return outcome == DiscoveryImportOutcome.NotFound ? NotFound() : Ok();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { error = "invalid_result", exception.Message });
        }
    }

    private bool HasValidBearerToken()
    {
        var expected = _options.SharedSecret;
        var authorization = Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";

        if (string.IsNullOrWhiteSpace(expected) ||
            !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var supplied = authorization[prefix.Length..].Trim();
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
