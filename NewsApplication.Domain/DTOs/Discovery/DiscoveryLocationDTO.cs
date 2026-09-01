namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// The target the run was dispatched for, echoed back. Only Iso2 is guaranteed: a
/// country-level run has no city, so City, CityLocalName and CityId are all null together.
/// </summary>
public sealed record DiscoveryLocationDTO
{
    public string? Iso2 { get; init; }

    public string? Iso3 { get; init; }

    public string? CountryName { get; init; }

    public string? City { get; init; }

    /// <summary>The endonym ("Скопје"). Comes from City.LocalName and exists nowhere else;
    /// the pipeline builds local-language search queries from it.</summary>
    public string? CityLocalName { get; init; }

    /// <summary>City.Id as sent on dispatch. String, not Guid — an unparseable value should
    /// fail import with a message rather than throw in the deserializer.</summary>
    public string? CityId { get; init; }
}
