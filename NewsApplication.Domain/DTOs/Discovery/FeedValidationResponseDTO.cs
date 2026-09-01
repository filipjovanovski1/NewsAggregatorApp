namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>Response envelope returned by POST /feeds/validate.</summary>
public sealed record FeedValidationResponseDTO
{
    public List<FeedValidationResultDTO> Results { get; init; } = new();
}
