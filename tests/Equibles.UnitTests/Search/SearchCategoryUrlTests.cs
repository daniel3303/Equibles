using Equibles.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;

namespace Equibles.UnitTests.Search;

public class SearchCategoryUrlTests
{
    [Fact]
    public void CategoryUrl_ShippedGroupWithNoBrowsePage_ReturnsNull()
    {
        // Contract (XML doc): CategoryUrl is the "See all" destination for a group,
        // "or null when no browse page exists". "Insiders" is a real shipped search
        // group (InsiderOwnerSearchProvider.Category) with no browse page, so it must
        // return null even though the URL helper can resolve any action.
        var url = Substitute.For<IUrlHelper>();
        url.Action(Arg.Any<UrlActionContext>()).Returns("/resolved/path");

        var result = url.CategoryUrl("Insiders", "berkshire");

        result.Should().BeNull();
    }
}
