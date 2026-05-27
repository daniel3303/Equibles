using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Holdings;

namespace Equibles.UnitTests.Web;

public class HoldingsScreenerControllerToCriteriaNullIndustryIdsTests
{
    // ToCriteria maps the user-supplied ScreenerCriteriaViewModel (a form-bound
    // POCO) to a ScreenerCriteria (the repository-layer query DTO). Every
    // field is a straight copy — EXCEPT IndustryIds, which is defensively
    // null-coalesced to an empty list:
    //   IndustryIds = filters.IndustryIds ?? []
    //
    // The view-model's default property initializer is `[]`, but model-
    // binding can leave the property null in two production scenarios:
    //   • Old-format submitted form data that doesn't include the field
    //     (a stale browser tab with cached form HTML predating the
    //     IndustryIds checkbox group).
    //   • Direct controller calls from test fixtures or integration code
    //     that construct the view model manually with `new()` followed
    //     by partial-field initialisers that don't touch IndustryIds.
    // The downstream repository query enumerates IndustryIds — a null
    // value NREs on `Where(s => criteria.IndustryIds.Contains(s.Industry))`.
    //
    // The risk this pin uniquely catches: ToCriteria is internal static
    // and currently untested. A "tidy the redundant ?? []" refactor —
    // under the (false) intuition that the view-model's default
    // initializer guarantees non-null — would compile, work for every
    // form-bound case (model binding respects the initializer), and
    // NRE on the two production scenarios above. The repository-side
    // null-tolerance is the LAST line of defence; without this guard,
    // every screener query from a partially-constructed criteria
    // crashes.
    //
    // Pin: invoke ToCriteria with a view-model whose IndustryIds has
    // been explicitly set to null. Assert the resulting
    // ScreenerCriteria.IndustryIds is non-null and empty. Distinguishes:
    //   • Working `?? []`: returns non-null empty list.
    //   • Dropped `?? []`: returns null (fails NotBeNull).
    //   • Wrong-default refactor — `?? new List<Guid> { Guid.Empty }`:
    //     returns non-null but non-empty (fails BeEmpty).
    //
    // Reflection-invoke since ToCriteria is internal static.
    [Fact]
    public void ToCriteria_NullIndustryIds_DefaultsToNonNullEmptyList()
    {
        var method = typeof(HoldingsScreenerController).GetMethod(
            "ToCriteria",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var filters = new ScreenerCriteriaViewModel { IndustryIds = null };

        var result = (ScreenerCriteria)method!.Invoke(null, [filters]);

        result.IndustryIds.Should().NotBeNull();
        result.IndustryIds.Should().BeEmpty();
    }
}
