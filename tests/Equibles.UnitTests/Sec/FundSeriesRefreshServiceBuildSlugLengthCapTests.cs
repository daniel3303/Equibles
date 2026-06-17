using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FundSeriesRefreshServiceBuildSlugLengthCapTests
{
    // Contract: a fund's slug routes its profile and must stay within the 200-char column
    // cap, but the discriminator is what disambiguates two funds whose names slugify the
    // same — so an over-long name must be truncated, never the discriminator. A naive cap
    // that trimmed the whole slug would chop the discriminator and collide distinct funds.
    [Fact]
    public void BuildSlug_NameLongerThanCap_TruncatesNameAndKeepsFullDiscriminator()
    {
        var name = new string('a', 250);
        const string discriminator = "seriesid-s000123";

        var method = typeof(FundSeriesRefreshService).GetMethod(
            "BuildSlug",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var slug = (string)method!.Invoke(null, [name, discriminator]);

        slug.Length.Should().BeLessThanOrEqualTo(200, "the slug must fit the 200-char cap");
        slug.Should()
            .EndWith($"-{discriminator}", "the disambiguating discriminator is never truncated");
    }
}
