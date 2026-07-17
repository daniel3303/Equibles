using System.Reflection;
using Equibles.Holdings.Repositories;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins NormalizeCikQuery, the fix behind the MCP audit's unresolvable zero-padded CIK
/// finding: CIKs are stored unpadded but the SEC-canonical form zero-pads them to 10 digits
/// ('0001067983' is the documented example on GetFundCloneBacktest and the form EDGAR hands
/// an LLM), so an all-digit query strips its leading zeros before becoming the CIK prefix.
/// A non-digit query passes through untouched, and an all-zero query must NOT trim to an
/// empty prefix — that would turn the CIK pattern into a match-everything '%'.
/// (The companion largest-first ranking is pinned in the integration suite —
/// InstitutionalHolderRepositorySearchNameOrCikLargestFirstTests — because the search uses
/// EF.Functions.ILike, which only runs on the real PostgreSQL provider.)
/// </summary>
public class InstitutionalHolderRepositorySearchRankingTests
{
    [Theory]
    [InlineData("0001067983", "1067983")]
    [InlineData("1067983", "1067983")]
    [InlineData("0001067", "1067")]
    [InlineData("berk", "berk")]
    [InlineData("000", "000")] // all zeros must NOT trim to a match-everything empty prefix
    public void NormalizeCikQuery_TrimsLeadingZerosFromAllDigitQueriesOnly(
        string input,
        string expected
    )
    {
        var method = typeof(InstitutionalHolderRepository).GetMethod(
            "NormalizeCikQuery",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var normalized = (string)method.Invoke(null, [input])!;

        normalized.Should().Be(expected);
    }
}
