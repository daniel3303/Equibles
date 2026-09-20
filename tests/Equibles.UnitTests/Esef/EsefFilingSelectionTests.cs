using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Equibles.Integrations.XbrlFilings;
using Equibles.Integrations.XbrlFilings.Models;
using Equibles.Sec.Data.Models;
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
    public void HistorySelectsExactlyOneReportPerPeriodNewestFirst()
    {
        var history = EsefFilingSelection.PickHistory(OneIssuer(), "FR");

        history.Should().HaveCount(4);
        history
            .Select(filing => filing.PeriodEnd)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeInDescendingOrder();
        history.Should().OnlyContain(filing => filing.CountryCode == "FR");
        EsefFilingSelection
            .PickHistory(OneIssuer().Reverse(), "FR")
            .Select(filing => filing.FilingKey)
            .Should()
            .Equal(history.Select(filing => filing.FilingKey));
    }

    [Fact]
    public void EmptyHistoryIsSafe()
    {
        EsefFilingSelection.PickHistory(null, "FR").Should().BeEmpty();
        EsefFilingSelection.PickHistory([], "FR").Should().BeEmpty();
    }

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

    // AccessionNumber holds 32 characters and this key is exactly 32, with nothing to spare. Read against
    // the column's own declared width, so widening or narrowing it cannot pass unnoticed.
    [Fact]
    public void TheFilingReferenceFillsTheColumnExactly()
    {
        var reference = EsefFilingSelection.FilingReference(
            EsefFilingSelection.PickLatest(OneIssuer(), "FR")
        );

        reference.Should().Be("529900S21EQ1BO4ESM68-20251231-FR");
        reference
            .Length.Should()
            .Be(EsefFilingSelection.FilingReferenceLength)
            .And.Be(ColumnWidth(nameof(Document.AccessionNumber)));
    }

    private static int ColumnWidth(string property) =>
        typeof(Document)
            .GetProperty(property)
            .GetCustomAttributes(typeof(MaxLengthAttribute), false)
            .Cast<MaxLengthAttribute>()
            .Single()
            .Length;

    // A filing whose country the index leaves out, or states in some other shape, cannot be told from its
    // siblings and its key cannot be built. It is refused as ineligible, so the reference is never asked
    // for and the pass never faults.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("F")]
    [InlineData("FRA")]
    public void AFilingWithoutATwoLetterCountryIsNotEligible(string country)
    {
        var countryless = OneIssuer()
            .Select(filing => filing with { CountryCode = country })
            .ToList();

        EsefFilingSelection.IsEsefWithLegalEntityIdentifier(countryless[0]).Should().BeFalse();
        EsefFilingSelection.PickLatest(countryless, "FR").Should().BeNull();
        EsefFilingSelection.PickForPeriod(countryless, "FR").Should().BeNull();
    }

    // The key is stored, so it must read the same on every host. A Buddhist-era calendar would otherwise
    // spell 2025 as 2568 and the lane would re-capture the same report every cycle.
    [Fact]
    public void TheFilingReferenceIsTheSameUnderANonGregorianCalendar()
    {
        var latest = EsefFilingSelection.PickLatest(OneIssuer(), "FR");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            EsefFilingSelection
                .FilingReference(latest)
                .Should()
                .Be("529900S21EQ1BO4ESM68-20251231-FR");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
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
