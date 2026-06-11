using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Contract (SecSgmlEnvelope.cs:35-37): the helper returns the "full
/// single-line value of an SGML header tag, whitespace-normalized but with
/// every token kept". A tab-separated multi-word form type ("DEF\t14A") is
/// the diagnostic input: a bug in the split char set, the join delimiter, or
/// the line-walk bounds surfaces as a wrong return value — single space
/// joined "DEF 14A" is the only value that satisfies the contract.
/// </summary>
public class SecSgmlEnvelopeTryGetTagLineTabSeparatorTests
{
    [Fact]
    public void TryGetTagLine_TabSeparatedMultiWord_NormalizesToSingleSpaceJoinedTokens()
    {
        var block = "<TYPE>DEF\t14A\n<FILENAME>proxy.htm\n";

        var found = SecSgmlEnvelope.TryGetTagLine(block, "TYPE", out var value);

        found.Should().BeTrue();
        value.Should().Be("DEF 14A");
    }
}
