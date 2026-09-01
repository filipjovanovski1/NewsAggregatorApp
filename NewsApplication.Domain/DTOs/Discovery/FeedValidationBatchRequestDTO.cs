namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>Request envelope for POST /feeds/validate. The pipeline accepts 1-500 feeds.</summary>
public sealed record FeedValidationBatchRequestDTO
{
    public List<FeedValidationRequestDTO> Feeds { get; init; } = new();
}
