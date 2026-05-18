using Equibles.Search.Abstractions;
using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class SearchCategoryRouteMappingTests
{
    // Pins the linkable allowlist: every shipped Kind must resolve to a URL. A new provider Kind
    // is a conscious decision here, never a silently inert search result.
    [Theory]
    [InlineData("Stock", true)]
    [InlineData("Filing", true)]
    [InlineData("EconomicSeries", true)]
    [InlineData("FuturesMarket", true)]
    [InlineData("Institution", true)]
    [InlineData("Insider", true)]
    [InlineData("CongressMember", true)]
    public void HitUrl_ResolvesLinkableKinds_AndReturnsNullForKnownNonLinkable(
        string kind,
        bool expectLink
    )
    {
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Any<UrlActionContext>()).Returns("/resolved/path");
        var hit = new SearchHit { Kind = kind };

        var result = url.HitUrl(hit);

        if (expectLink)
        {
            result.Should().Be("/resolved/path");
        }
        else
        {
            result.Should().BeNull();
        }
    }

    [Fact]
    public void HitUrl_UnknownKind_ReturnsNull()
    {
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Any<UrlActionContext>()).Returns("/resolved/path");

        url.HitUrl(new SearchHit { Kind = "SomethingNew" }).Should().BeNull();
    }
}
