namespace NewsApplication.Domain.Helpers;

using System.Text.RegularExpressions;

public static class TitleNormalizer
{
    private static readonly Regex CollapseWhitespace = new("\\s+", RegexOptions.Compiled);

    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        var trimmed = title.Trim();
        return CollapseWhitespace.Replace(trimmed, " ");
    }
}