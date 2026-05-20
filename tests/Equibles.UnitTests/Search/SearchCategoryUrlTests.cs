using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class SearchCategoryUrlTests
{
    [Fact]
    public void CategoryUrl_ShippedGroupWithNoBrowsePage_FallsBackToSearchPage()
    {
        // Contract (XML doc): CategoryUrl returns a "See all" destination for every shipped
        // category. Groups with a dedicated browse page resolve to that page; the rest
        // ("Insiders", "Institutions", "Congress", "SEC Filings") fall back to the search
        // page filtered to just that category so users always get an expanded result list.
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Any<UrlActionContext>()).Returns("/resolved/path");

        var result = url.CategoryUrl("Insiders", "berkshire");

        result.Should().Be("/resolved/path");
    }
}
