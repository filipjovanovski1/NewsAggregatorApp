using System.Text.Json;

namespace NewsApplication.Domain.DTOs.Discovery;

/// <summary>
/// The one set of serializer options the discovery wire contract is read with.
///
/// This exists as shared state rather than a private field on the callback controller so that
/// the tests deserialize through the exact same configuration production does — a test with
/// its own local options can only prove that *some* options parse the sample.
///
/// It is deliberately NOT registered globally. There is no JsonNamingPolicy configured
/// anywhere in this solution, so every existing endpoint serves .NET's default camelCase to
/// the React client; setting PropertyNamingPolicy on the global JsonOptions would silently
/// reshape every existing API response. [FromBody] model binding reads those global options
/// and cannot be overridden per-controller, so the callback action deserializes Request.Body
/// with these options by hand instead.
/// </summary>
public static class DiscoveryJsonOptions
{
    public static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}