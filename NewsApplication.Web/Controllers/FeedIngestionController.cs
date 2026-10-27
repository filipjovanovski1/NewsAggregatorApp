using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NewsApplication.Domain.DTOs.Ingestion;
using NewsApplication.Repository.Db.Interfaces;

namespace NewsApplication.Web.Controllers;

[ApiController]
[Route("api/internal/feed-ingestion")]
public sealed class FeedIngestionController : ControllerBase
{
    private readonly IFeedIngestionRepository _repository;
    private readonly IConfiguration _configuration;

    public FeedIngestionController(
        IFeedIngestionRepository repository,
        IConfiguration configuration)
    {
        _repository = repository;
        _configuration = configuration;
    }

    [HttpPost("claims")]
    public async Task<IActionResult> Claim(
        [FromBody] FeedClaimRequestDTO request,
        CancellationToken ct)
    {
        if (!HasValidBearerToken())
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.WorkerId) ||
            request.MaxItems is < 1 or > 100 ||
            request.LeaseSeconds is < 30 or > 1800)
            return BadRequest(new { error = "invalid_claim_request" });

        var claims = await _repository.ClaimAsync(request, DateTimeOffset.UtcNow, ct);
        return Ok(new FeedClaimResponseDTO(claims));
    }

    [HttpPost("claims/{leaseId:guid}/complete")]
    public async Task<IActionResult> Complete(
        Guid leaseId,
        [FromBody] CompleteFeedPollDTO request,
        CancellationToken ct)
    {
        if (!HasValidBearerToken())
            return Unauthorized();

        var key = Request.Headers["Idempotency-Key"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256)
            return BadRequest(new { error = "invalid_idempotency_key" });
        if (request.Feed is null || request.Articles is null)
            return BadRequest(new { error = "invalid_completion" });

        var outcome = await _repository.CompleteAsync(
            leaseId, key, request, DateTimeOffset.UtcNow, ct);
        return Result(outcome);
    }

    [HttpPost("claims/{leaseId:guid}/fail")]
    public async Task<IActionResult> Fail(
        Guid leaseId,
        [FromBody] FailFeedPollDTO request,
        CancellationToken ct)
    {
        if (!HasValidBearerToken())
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.ErrorType) ||
            string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "invalid_failure" });

        var outcome = await _repository.FailAsync(leaseId, request, DateTimeOffset.UtcNow, ct);
        return Result(outcome);
    }

    private IActionResult Result(FeedIngestionWriteOutcome outcome) => outcome switch
    {
        FeedIngestionWriteOutcome.Applied => Ok(),
        FeedIngestionWriteOutcome.AlreadyApplied => Ok(new { duplicate = true }),
        FeedIngestionWriteOutcome.NotFound => NotFound(),
        FeedIngestionWriteOutcome.StaleLease => Conflict(new { error = "stale_lease" }),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private bool HasValidBearerToken()
    {
        var expected = _configuration["FeedIngestion:Token"];
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
