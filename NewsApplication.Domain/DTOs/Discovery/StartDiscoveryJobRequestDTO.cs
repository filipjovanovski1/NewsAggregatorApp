namespace NewsApplication.Domain.DTOs.Discovery;

public sealed record StartDiscoveryJobRequestDTO
{
    public Guid JobId { get; init; }
    public string CallbackUrl { get; init; } = null!;
    public string Iso2 { get; init; } = null!;
    public string? Iso3 { get; init; }
    public string? CountryName { get; init; }
    public string? City { get; init; }
    public string? CityLocalName { get; init; }
    public Guid? CityId { get; init; }
    public List<string> KnownDomains { get; init; } = new();
}
