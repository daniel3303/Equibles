using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// SelectPersistable enforces the extractor's contract boundary with the
/// Company Facts API: only facts carrying at least one explicit dimension are
/// persisted (the API owns the consolidated context — admitting a
/// no-dimension fact here would fight the API import over the same
/// unique-index slot), concepts must belong to a known taxonomy, and values
/// must fit their columns rather than being truncated into corrupt keys.
/// </summary>
public class XbrlFactExtractionServiceSelectPersistableTests
{
    private static ParsedXbrlFact Fact(
        string taxonomy = "us-gaap",
        string tag = "RevenueFromContractWithCustomerExcludingAssessedTax",
        string unit = "USD",
        List<ParsedXbrlDimension> dimensions = null
    ) =>
        new()
        {
            Taxonomy = taxonomy,
            Tag = tag,
            Unit = unit,
            Value = 1_000m,
            IsInstant = false,
            PeriodStart = new DateOnly(2025, 9, 29),
            PeriodEnd = new DateOnly(2025, 12, 27),
            Dimensions = dimensions ?? [],
        };

    private static List<ParsedXbrlDimension> IPhoneCut() =>
        [
            new ParsedXbrlDimension
            {
                Axis = "srt:ProductOrServiceAxis",
                Member = "aapl:IPhoneMember",
            },
        ];

    [Fact]
    public void SelectPersistable_DimensionalKnownTaxonomyFact_IsKeptWithComputedKey()
    {
        var selected = XbrlFactExtractionService.SelectPersistable([Fact(dimensions: IPhoneCut())]);

        selected.Should().ContainSingle();
        selected[0].Taxonomy.Should().Be(FactTaxonomy.UsGaap);
        selected[0].DimensionsKey.Should().NotBeEmpty();
    }

    [Fact]
    public void SelectPersistable_ConsolidatedFact_IsDropped()
    {
        XbrlFactExtractionService.SelectPersistable([Fact(dimensions: [])]).Should().BeEmpty();
    }

    [Fact]
    public void SelectPersistable_FilerExtensionConcept_IsDropped()
    {
        // The concept itself lives in a filer-extension namespace; FactTaxonomy
        // cannot represent it yet, so it must be skipped, not misfiled.
        var fact = Fact(taxonomy: "aapl", dimensions: IPhoneCut());

        XbrlFactExtractionService.SelectPersistable([fact]).Should().BeEmpty();
    }

    [Fact]
    public void SelectPersistable_UnitExceedingColumnLength_IsDropped()
    {
        var fact = Fact(unit: new string('X', 33), dimensions: IPhoneCut());

        XbrlFactExtractionService.SelectPersistable([fact]).Should().BeEmpty();
    }
}
