using System.Reflection;
using Equibles.Web.Controllers;

namespace Equibles.UnitTests.Web;

/// <summary>
/// Adversarial Lane A. <c>NormalizeCiks</c> dedups with
/// <c>StringComparer.OrdinalIgnoreCase</c> — the inline comment two lines
/// below the helper makes the case-insensitivity load-bearing: "Case-
/// insensitive comparer matches NormalizeCiks above; otherwise a lowercase
/// <c>?ciks=cik123</c> in the URL would miss the row stored as
/// <c>CIK123</c>." The sibling <c>WhitespacePaddedDuplicate</c> test only
/// pins trim-then-dedup; a refactor that swapped the comparer to
/// <c>StringComparer.Ordinal</c> (or dropped the explicit comparer
/// altogether, defaulting to ordinal value-equality on <c>Distinct</c>)
/// would still pass that test but would admit the same logical CIK twice
/// into the <c>WHERE Cik IN (…)</c> on the comparison/overlap views,
/// double-counting holdings in the join.
/// </summary>
public class ProfilesControllerNormalizeCiksCaseInsensitiveTests
{
    private static readonly MethodInfo NormalizeMethod = typeof(ProfilesController).GetMethod(
        "NormalizeCiks",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void NormalizeCiks_DuplicatesDifferingOnlyInLetterCase_CollapseToOneEntry()
    {
        // Three inputs that are byte-distinct but logically the same CIK
        // once compared case-insensitively. Exactly one survivor is required.
        var result =
            (List<string>)NormalizeMethod.Invoke(null, [new[] { "CIK123", "cik123", "CiK123" }]);

        result
            .Should()
            .ContainSingle(
                "OrdinalIgnoreCase Distinct must collapse case-variant CIK duplicates so the holdings IN-clause doesn't join the same filer twice"
            );
    }
}
