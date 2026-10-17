using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// SelectPersistable enforces the extractor's contract boundary with the
/// Company Facts API: standard-taxonomy facts are persisted only when they
/// carry at least one explicit dimension (the API owns their consolidated
/// context — admitting a no-dimension fact here would fight the API import
/// over the same unique-index slot), filer-extension concepts are persisted
/// at any dimensionality (the API never carries them), and values must fit
/// their columns rather than being truncated into corrupt keys.
/// </summary>
public class XbrlFactExtractionServiceSelectPersistableTests
{
    private static ParsedXbrlFact Fact(
        string taxonomy = "us-gaap",
        string tag = "RevenueFromContractWithCustomerExcludingAssessedTax",
        string unit = "USD",
        string ns = "http://fasb.org/us-gaap/2023",
        List<ParsedXbrlDimension> dimensions = null
    ) =>
        new()
        {
            Taxonomy = taxonomy,
            Tag = tag,
            Namespace = ns,
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
    public void SelectPersistable_ConsolidatedStandardTaxonomyFact_IsDropped()
    {
        XbrlFactExtractionService.SelectPersistable([Fact(dimensions: [])]).Should().BeEmpty();
    }

    // A 10-Q/10-K fills a consolidated standard fact only while Company Facts lacks its accession
    // (periodicFill), and only from an unqualified context of one of the issuer's own CIKs.
    [Theory]
    [InlineData("TenQ", true, "0001517175", null, true)]
    [InlineData("TenQa", true, "1517175", null, true)]
    [InlineData("TenK", true, "0001517175", null, true)]
    [InlineData("TenKa", true, "0001517175", null, true)]
    [InlineData("TenQ", true, "0000320193", "0001517175", true)]
    [InlineData("TenQ", false, "0001517175", null, false)]
    [InlineData("TenQ", true, "0000320193", null, false)]
    [InlineData("EightK", true, "0001517175", null, false)]
    public void SelectPersistable_DomesticPeriodicConsolidatedFact_IsKeptOnlyForAnIssuerCikWhileFilling(
        string form,
        bool periodicFill,
        string cik,
        string secondaryCik,
        bool kept
    )
    {
        var fact = new ParsedXbrlFact
        {
            Taxonomy = "us-gaap",
            Tag = "EarningsPerShareDiluted",
            Namespace = "http://fasb.org/us-gaap/2025",
            Unit = "USD/shares",
            Value = 0.76m,
            PeriodStart = new DateOnly(2026, 3, 28),
            PeriodEnd = new DateOnly(2026, 6, 26),
            ConsolidatedCik = "0001517175",
        };
        var document = new Document
        {
            DocumentType = DocumentType.FromValue(form),
            Issuer = new EquityIssuer
            {
                Cik = cik,
                SecondaryCiks = secondaryCik == null ? [] : [secondaryCik],
            },
        };

        XbrlFactExtractionService
            .SelectPersistable([fact], document, periodicFill)
            .Should()
            .HaveCount(kept ? 1 : 0);
    }

    [Theory]
    [InlineData("dei", "http://xbrl.sec.gov/dei/2025")]
    [InlineData("us-gaap", "https://example.org/us-gaap")]
    public void SelectPersistable_DomesticFillOutsideAFinancialTaxonomy_IsDropped(
        string taxonomy,
        string ns
    )
    {
        var fact = new ParsedXbrlFact
        {
            Taxonomy = taxonomy,
            Tag = "EntityCommonStockSharesOutstanding",
            Namespace = ns,
            Unit = "shares",
            Value = 40_796_083m,
            IsInstant = true,
            PeriodStart = new DateOnly(2026, 7, 24),
            PeriodEnd = new DateOnly(2026, 7, 24),
            ConsolidatedCik = "0001517175",
        };
        var document = new Document
        {
            DocumentType = DocumentType.TenQ,
            Issuer = new EquityIssuer { Cik = "0001517175" },
        };

        XbrlFactExtractionService.SelectPersistable([fact], document, true).Should().BeEmpty();
    }

    [Fact]
    public void SelectPersistable_FilerExtensionConcept_IsKeptWithQNameTag()
    {
        // The concept lives in the company's own namespace — the Company Facts
        // API never carries it, so this extractor is its only source. Even the
        // consolidated (no-dimension) context is persisted, and the storage tag
        // keeps the prefix so extension concepts never collide across filers.
        var fact = Fact(
            taxonomy: "aapl",
            tag: "SubscriberCount",
            ns: "http://www.apple.com/20230930",
            dimensions: []
        );

        var selected = XbrlFactExtractionService.SelectPersistable([fact]);

        selected.Should().ContainSingle();
        selected[0].Taxonomy.Should().Be(FactTaxonomy.Custom);
        selected[0].Tag.Should().Be("aapl:SubscriberCount");
        selected[0].DimensionsKey.Should().BeEmpty();
    }

    [Fact]
    public void SelectPersistable_DimensionalFilerExtensionConcept_IsKept()
    {
        var fact = Fact(
            taxonomy: "aapl",
            tag: "SubscriberCount",
            ns: "http://www.apple.com/20230930",
            dimensions: IPhoneCut()
        );

        var selected = XbrlFactExtractionService.SelectPersistable([fact]);

        selected.Should().ContainSingle();
        selected[0].Taxonomy.Should().Be(FactTaxonomy.Custom);
        selected[0].DimensionsKey.Should().NotBeEmpty();
    }

    [Fact]
    public void SelectPersistable_UnknownPrefixWithoutNamespace_IsDropped()
    {
        // A prefix the document never declares cannot be attributed to anyone —
        // skip it rather than misfile it as a company concept.
        var fact = Fact(taxonomy: "aapl", ns: null, dimensions: IPhoneCut());

        XbrlFactExtractionService.SelectPersistable([fact]).Should().BeEmpty();
    }

    [Fact]
    public void SelectPersistable_ReferenceTaxonomyConcept_IsDropped()
    {
        // SEC-hosted reference taxonomies (country, currency, exch, …) are not
        // company concepts; their namespace ownership keeps them out of Custom.
        var fact = Fact(
            taxonomy: "ecd",
            tag: "PeoTotalCompAmt",
            ns: "http://xbrl.sec.gov/ecd/2023",
            dimensions: []
        );

        XbrlFactExtractionService.SelectPersistable([fact]).Should().BeEmpty();
    }

    [Fact]
    public void SelectPersistable_FilerExtensionTagExceedingColumnAfterPrefixing_IsDropped()
    {
        // The stored tag is "prefix:Tag" — the length gate must apply to that
        // composed value, not the raw local name.
        var fact = Fact(
            taxonomy: "adbe",
            tag: new string('X', 253),
            ns: "http://www.adobe.com/20231201",
            dimensions: []
        );

        XbrlFactExtractionService.SelectPersistable([fact]).Should().BeEmpty();
    }

    [Fact]
    public void SelectPersistable_UnitExceedingColumnLength_IsDropped()
    {
        var fact = Fact(unit: new string('X', 33), dimensions: IPhoneCut());

        XbrlFactExtractionService.SelectPersistable([fact]).Should().BeEmpty();
    }
}
