using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using FluentAssertions;

namespace Equibles.UnitTests.Sec;

public class FormAdvPrivateFundArchiveImporterFilingsTests
{
    private const int Crd = 105958;

    [Fact]
    public void Filings_KeepARepeatOfUnchangedFigures_SoItsDateShowsTheyAreRecent()
    {
        // An adviser lists the same funds in 2021 and again in 2024: dropping the repeat would
        // leave the 2021 filing as the newest and age the figures out of the lookback.
        var filings = new[]
        {
            new FormAdvFilingRow(1, Crd, new DateOnly(2020, 3, 30)),
            new FormAdvFilingRow(2, Crd, new DateOnly(2021, 3, 30)),
            new FormAdvFilingRow(3, Crd, new DateOnly(2024, 3, 28)),
        };
        var funds = new Dictionary<int, FormAdvPrivateFundSums>
        {
            [2] = Hedge(1_000),
            [3] = Hedge(1_000),
        };

        var kept = FormAdvPrivateFundArchiveImporter.Filings(
            filings,
            funds,
            FormAdvRegistration.Registered
        );

        kept.Select(filing => filing.FilingId)
            .Should()
            .Equal([2, 3], "a filing before the first listing funds says nothing");
        kept[^1].FiledOn.Should().Be(new DateOnly(2024, 3, 28));
        kept[^1].HedgeFundGrossAssets.Should().Be(1_000);
    }

    [Fact]
    public void Filings_AFilingWithoutFundsAfterSome_RecordsThatNoneWereListed()
    {
        var filings = new[]
        {
            new FormAdvFilingRow(1, Crd, new DateOnly(2021, 3, 30)),
            new FormAdvFilingRow(2, Crd, new DateOnly(2022, 3, 30)),
        };
        var funds = new Dictionary<int, FormAdvPrivateFundSums> { [1] = Hedge(500) };

        var kept = FormAdvPrivateFundArchiveImporter.Filings(
            filings,
            funds,
            FormAdvRegistration.ExemptReporting
        );

        kept.Should().HaveCount(2);
        kept[1].FundCount.Should().Be(0);
        kept[1].Registration.Should().Be(FormAdvRegistration.ExemptReporting);
    }

    private static FormAdvPrivateFundSums Hedge(long grossAssets)
    {
        var sums = new FormAdvPrivateFundSums();
        sums.Add(FormAdvPrivateFundType.Hedge, grossAssets);
        return sums;
    }
}
