using Equibles.Integrations.XbrlFilings;
using Equibles.Integrations.XbrlFilings.Models;
using FluentAssertions;

namespace Equibles.UnitTests.Esef;

// One issuer-period holds several filings; see TestAssets/Esef/README.md for the captured shape.
public class EsefFilingSelectionTests
{
    private static readonly Uri Origin = new("https://filings.xbrl.org");

    private static IReadOnlyList<XbrlFiling> OneIssuer() =>
        XbrlFilingsParser
            .Read(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "TestAssets",
                        "Esef",
                        "filings-one-issuer.json"
                    )
                ),
                Origin
            )
            .Filings;

    [Fact]
    public void OneIssuerPeriodReallyDoesHoldSeveralFilings()
    {
        OneIssuer()
            .Where(filing => filing.PeriodEnd == new DateOnly(2024, 12, 31))
            .Select(filing => filing.CountryCode)
            .Should()
            .BeEquivalentTo(["FR", "GB"]);
    }

    [Fact]
    public void TheIssuersOwnMarketWinsOverTheEarlierAddition()
    {
        var period = OneIssuer().Where(filing => filing.PeriodEnd == new DateOnly(2024, 12, 31));

        EsefFilingSelection.PickForPeriod(period, "FR").CountryCode.Should().Be("FR");
        EsefFilingSelection.PickForPeriod(period, "GB").CountryCode.Should().Be("GB");
    }

    // With no market stated the rule must still be total, or the same corpus yields a different
    // choice from one pass to the next and the period's facts are counted twice.
    [Fact]
    public void WithNoMarketStatedTheChoiceIsStillDeterministic()
    {
        var period = OneIssuer().Where(filing => filing.PeriodEnd == new DateOnly(2024, 12, 31));

        var first = EsefFilingSelection.PickForPeriod(period, null);
        var again = EsefFilingSelection.PickForPeriod(period.Reverse(), null);

        first.FilingKey.Should().Be(again.FilingKey);
    }

    [Fact]
    public void TheLatestPeriodIsChosenBeforeTheCountry()
    {
        EsefFilingSelection
            .PickLatest(OneIssuer(), "FR")
            .Should()
            .Match<XbrlFiling>(filing =>
                filing.PeriodEnd == new DateOnly(2025, 12, 31) && filing.CountryCode == "FR"
            );
    }

    // AccessionNumber holds 32 characters and this key is exactly 32, with nothing to spare.
    [Fact]
    public void TheFilingReferenceFillsTheColumnExactly()
    {
        var reference = EsefFilingSelection.FilingReference(
            EsefFilingSelection.PickLatest(OneIssuer(), "FR")
        );

        reference.Should().Be("529900S21EQ1BO4ESM68-20251231-FR");
        reference.Length.Should().Be(EsefFilingSelection.FilingReferenceLength).And.Be(32);
    }

    // The same issuer's two countries must not collapse to one reference, or picking one filing
    // per period would be pointless.
    [Fact]
    public void TwoCountriesOfOnePeriodGetDifferentReferences()
    {
        var period = OneIssuer()
            .Where(filing => filing.PeriodEnd == new DateOnly(2024, 12, 31))
            .Select(EsefFilingSelection.FilingReference)
            .ToList();

        period.Should().OnlyHaveUniqueItems().And.HaveCount(2);
    }
}
