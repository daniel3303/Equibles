using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FundSeriesRefreshServiceBuildSlugDiscriminatorOverflowTests
{
    // Mirror of the long-name cap pin, attacking the opposite overflow: when the discriminator
    // ALONE exceeds the 200-char slug cap there is no room for the name, so BuildSlug must still
    // return a slug within the cap (it backs the unique Slug column) by truncating the
    // discriminator itself and dropping the name — never emit an over-cap slug that the column
    // rejects. A naive build that always appended "{name}-{discriminator}" would overflow here.
    [Fact]
    public void BuildSlug_DiscriminatorLongerThanCap_TruncatesToCapAndDropsName()
    {
        const string name = "iShares Test Fund";
        var discriminator = new string('b', 250);

        var method = typeof(FundSeriesRefreshService).GetMethod(
            "BuildSlug",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var slug = (string)method!.Invoke(null, [name, discriminator]);

        slug.Length.Should()
            .BeLessThanOrEqualTo(
                200,
                "the slug must fit the cap even when the discriminator alone exceeds it"
            );
        slug.Should()
            .Be(
                new string('b', 200),
                "the over-cap discriminator is truncated and the name dropped for want of room"
            );
    }
}
