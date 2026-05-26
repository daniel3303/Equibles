using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesTryResolveSlashSeparatorTests
{
    // Normalize folds '/' → '-' so callers can use path-style phrasing
    // ("operating/income") that the MCP LLM consumer would generate when
    // mirroring a user input like "operating/income". A refactor that
    // pruned the Replace("/", "-") on the false intuition that "we already
    // accept dashes" would compile cleanly — every existing test passes —
    // and only the slash-form queries would silently miss the Map lookup
    // and return SupportedAliases-only suggestions. Pin the slash arm.
    [Fact]
    public void TryResolve_AliasWithForwardSlashSeparator_NormalizesToDashAndResolves()
    {
        var success = FinancialConceptAliases.TryResolve("operating/income", out var concepts);

        success.Should().BeTrue();
        concepts.Should().HaveCount(1);
        concepts[0].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        concepts[0].Tag.Should().Be("OperatingIncomeLoss");
    }
}
