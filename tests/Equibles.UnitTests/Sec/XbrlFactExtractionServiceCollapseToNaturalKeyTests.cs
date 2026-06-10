using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Filings render the same fact more than once (statement vs notes), often at
/// different precision (decimals="-6" in the statement, "-3" in a note).
/// Postgres rejects an upsert batch that targets the same unique-index slot
/// twice, so the collapse must keep exactly one row per natural key — and it
/// must keep the most precise rendering, not whichever the parser met first.
/// </summary>
public class XbrlFactExtractionServiceCollapseToNaturalKeyTests
{
    private static XbrlFactExtractionService.PersistableXbrlFact Candidate(
        decimal value,
        int? decimals
    )
    {
        var fact = new ParsedXbrlFact
        {
            Taxonomy = "us-gaap",
            Tag = "Revenues",
            Unit = "USD",
            Value = value,
            IsInstant = false,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 3, 31),
            Dimensions =
            [
                new ParsedXbrlDimension
                {
                    Axis = "srt:ProductOrServiceAxis",
                    Member = "aapl:IPhoneMember",
                },
            ],
            Decimals = decimals,
        };
        return new XbrlFactExtractionService.PersistableXbrlFact
        {
            Fact = fact,
            Taxonomy = Equibles.Sec.FinancialFacts.Data.Enums.FactTaxonomy.UsGaap,
            DimensionsKey = "samekey",
        };
    }

    [Fact]
    public void CollapseToNaturalKey_DuplicateSlot_KeepsHighestPrecisionRendering()
    {
        var rounded = Candidate(46_200_000_000m, -8);
        var precise = Candidate(46_222_000_000m, -6);

        var collapsed = XbrlFactExtractionService.CollapseToNaturalKey([rounded, precise]);

        collapsed.Should().ContainSingle();
        collapsed[0].Fact.Value.Should().Be(46_222_000_000m);
    }
}
