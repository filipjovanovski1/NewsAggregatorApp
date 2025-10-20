namespace NewsApplication.Web.Models
{
    // Used by /api/search
    public sealed class SearchResultDto
    {
        public string Kind { get; init; } = "";     // "country" | "city"
        public string IdOrIso { get; init; } = "";  // ISO2 for country, CityId as string for city
        public string Name { get; init; } = "";     // Country or City display name
        public double? Lat { get; init; }           // optional, mainly for city
        public double? Lng { get; init; }

        public string? CountryIso2 { get; init; }
    }

    // Used by /api/reverse
    public sealed class ReverseResultDto
    {
        public string Kind { get; init; } = "";     // "country" | "city"
        public string IdOrIso { get; init; } = "";
        public string Name { get; init; } = "";
        public double? Lat { get; init; }
        public double? Lng { get; init; }
        public string? CountryIso2 { get; init; }
    }
}
