using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

public class ProfilesControllerNormalizeCiksNullArrayTests
{
    // Sibling to ProfilesControllerNormalizeCiksTests (trim-then-dedup
    // ordering). That pin only exercises a NON-NULL array; this pin
    // defends the `(ciks ?? [])` null-coalesce guard on entry.
    //
    // The three call sites (ProfilesController.CompareInstitutions /
    // CombinedInstitutions / Insiders compare variants) all declare:
    //     [FromQuery(Name = "ciks")] string[] ciks = null
    // — so ASP.NET model binding hands NormalizeCiks a null array
    // whenever a visitor reaches the page without `?ciks=...`. The
    // bare-URL "Show me the comparison form" landing is the common
    // case, NOT an edge case: the user lands on the page first,
    // sees the empty form, then submits.
    //
    // The risk: a maintainer reads the method body and "simplifies"
    // the LINQ chain under the false intuition that "the array is
    // always supplied by model binding". Dropping `?? []` makes the
    // first .Where call NRE on every initial page visit — a 500 on
    // the empty-form landing page, observable only when the route
    // hits production traffic without a ciks query param.
    //
    // Contract: NormalizeCiks(null) returns an empty list. The
    // EMPTY-LIST assertion (not just NotThrow) distinguishes:
    //   • Working guard: returns [].
    //   • Drop-the-guard regression: throws NRE.
    //   • Wrong-default regression (e.g. `?? [""]`): returns
    //     non-empty list that downstream WHERE Cik IN (...) treats
    //     as a literal CIK lookup for "" — silently filters every
    //     row out, producing an empty results page where the user
    //     expected a comparison form.
    [Fact]
    public void NormalizeCiks_NullArray_ReturnsEmptyListInsteadOfThrowing()
    {
        var method = typeof(ProfilesController).GetMethod(
            "NormalizeCiks",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (List<string>)method.Invoke(null, [(string[])null]);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }
}
