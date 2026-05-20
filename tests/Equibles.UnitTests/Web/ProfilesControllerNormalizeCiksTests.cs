using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Pins the trim-then-dedup ordering inside ProfilesController.NormalizeCiks: two
/// CIK strings that differ only in surrounding whitespace MUST collapse to a single
/// entry. Tested via reflection because the helper is private to the controller.
/// </summary>
public class ProfilesControllerNormalizeCiksTests
{
    private static readonly MethodInfo NormalizeMethod = typeof(ProfilesController).GetMethod(
        "NormalizeCiks",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static List<string> Normalize(string[] ciks) =>
        (List<string>)NormalizeMethod.Invoke(null, [ciks]);

    [Fact]
    public void NormalizeCiks_WhitespacePaddedDuplicate_CollapsesToSingleEntry()
    {
        // ProfilesController.CompareInstitutions / CombinedInstitutions accept a
        // user-controlled `ciks=` array from the query string and pass it through
        // NormalizeCiks before issuing a WHERE Cik IN (...) on the institutional
        // holdings table. The contract its name promises — "Normalize" + the
        // dedup-list shape — is that surrounding whitespace is stripped BEFORE
        // duplicate detection, so a hand-typed `"1234567"` and a clipboard-pasted
        // `"  1234567  "` resolve to the same filer.
        //
        // The risk: a refactor that reorders the LINQ pipeline so `.Distinct()`
        // runs before `.Trim()` — for example pulling Distinct up the chain to
        // "fail fast on duplicates", or replacing the value-equality `Distinct`
        // with an `IEqualityComparer` that doesn't trim — would compile, pass
        // any test that only feeds clean inputs (every existing call site does),
        // and admit the same logical CIK twice into the SQL IN-clause. The
        // join then materialises the same filer's holdings twice on the
        // comparison/combined view, double-counting share totals and
        // mis-ordering the Jaccard-style overlap calculation downstream.
        //
        // Pin a single CIK with three superficially distinct whitespace
        // wrappings; the contract requires exactly one survivor.
        var result = Normalize(["1234567", "  1234567  ", "1234567\t"]);

        result.Should().ContainSingle();
        result[0].Trim().Should().Be("1234567");
    }
}
