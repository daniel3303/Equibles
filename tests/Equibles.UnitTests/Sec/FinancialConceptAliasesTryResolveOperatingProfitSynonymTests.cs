using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Statements;

namespace Equibles.UnitTests.Sec;

public class FinancialConceptAliasesTryResolveOperatingProfitSynonymTests
{
    // Pin the full normalize+synonym pipeline on a user phrase that requires
    // BOTH stages: "Operating Profit" must be lower-cased + space-replaced to
    // "operating-profit" (Normalize), THEN looked up in Synonyms as an alias
    // for "operating-income", THEN resolved through Map to
    // `us-gaap:OperatingIncomeLoss`. Pruning either step would silently
    // misroute every natural-English MCP query phrased as "Operating Profit"
    // — and "operating profit" vs "operating income" is exactly the kind of
    // pair a contributor "consolidating" the synonyms table might merge
    // (they're semantically the same accounting concept), only to discover
    // the tag swap broke historic comparisons.
    [Fact]
    public void TryResolve_OperatingProfitWithCapitalsAndSpace_ResolvesToOperatingIncomeLoss()
    {
        var success = FinancialConceptAliases.TryResolve("Operating Profit", out var concepts);

        success.Should().BeTrue();
        concepts.Should().HaveCount(1);
        concepts[0].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        concepts[0].Tag.Should().Be("OperatingIncomeLoss");
    }
}
