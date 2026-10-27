using NewsApplication.Domain.Helpers;

namespace NewsApplication.Tests.ArticleFetchTesting;

public sealed class StoredArticleScopeTests
{
    [Fact]
    public void CityOnlyQuery_DoesNotTreatCityAsArticleKeyword()
    {
        var result = ArticleScopeKey.Parse("city:skopje-mk|country:MK|q:Skopje");

        Assert.Equal("MK", result.CountryIso2);
        Assert.Equal("skopje-mk", result.CitySlug);
        Assert.Empty(result.Keywords);
    }

    [Fact]
    public void KeywordSuffix_WinsOverLocationQuery()
    {
        var result = ArticleScopeKey.Parse(
            "city:skopje-mk|country:MK|q:Skopje%20fire|kw:fire%20economy");

        Assert.Equal(new[] { "fire", "economy" }, result.Keywords);
    }

    [Fact]
    public void CountryQuery_UsesQueryAsKeywords()
    {
        var result = ArticleScopeKey.Parse("country:MK|q:sports");

        Assert.Equal(new[] { "sports" }, result.Keywords);
    }
}
