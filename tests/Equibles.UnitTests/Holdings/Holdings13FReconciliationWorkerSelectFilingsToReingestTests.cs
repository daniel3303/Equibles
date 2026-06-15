using Equibles.Holdings.HostedService;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FReconciliationWorkerSelectFilingsToReingestTests
{
    private static readonly DateOnly MinReportDate = new(2020, 1, 1);
    private static readonly DateOnly GlobalLatest = new(2026, 3, 31);

    private static FilingData Filing(
        string form,
        DateOnly reportDate,
        DateOnly filingDate,
        string accession
    ) =>
        new()
        {
            Form = form,
            ReportDate = reportDate,
            FilingDate = filingDate,
            AccessionNumber = accession,
        };

    // The BlackRock case: two 13F-HR quarters EDGAR lists but we hold none of —
    // both selected, in filing-date order; the quarter we already hold and the
    // 13F-NT notice are left out.
    [Fact]
    public void SelectFilingsToReingest_PicksMissing13FHRQuartersOnly()
    {
        var edgar = new[]
        {
            Filing("13F-HR", new(2025, 9, 30), new(2025, 11, 12), "ACC-SEP"), // already held
            Filing("13F-HR", new(2025, 12, 31), new(2026, 2, 12), "ACC-DEC"), // missing
            Filing("13F-HR", new(2026, 3, 31), new(2026, 5, 13), "ACC-MAR"), // missing
        };
        var ingested = new HashSet<DateOnly> { new(2025, 9, 30) };

        var result = Holdings13FReconciliationWorker.SelectFilingsToReingest(
            edgar,
            ingested,
            MinReportDate,
            GlobalLatest
        );

        result.Select(f => f.AccessionNumber).Should().Equal("ACC-DEC", "ACC-MAR");
    }

    // A 13F-NT notice (the filer reports through another manager — e.g. Vanguard
    // Group Inc for Q1 2026) is not a holdings report; never re-ingest it.
    [Fact]
    public void SelectFilingsToReingest_ExcludesNoticeFilings()
    {
        var edgar = new[] { Filing("13F-NT", new(2026, 3, 31), new(2026, 5, 8), "ACC-NT") };

        var result = Holdings13FReconciliationWorker.SelectFilingsToReingest(
            edgar,
            new HashSet<DateOnly>(),
            MinReportDate,
            GlobalLatest
        );

        result.Should().BeEmpty();
    }

    // Amendments are holdings reports too and must be picked up — ordered after
    // the original they restate so the import replays them in submission order.
    [Fact]
    public void SelectFilingsToReingest_IncludesAmendmentsOrderedByFilingDate()
    {
        var edgar = new[]
        {
            Filing("13F-HR/A", new(2026, 3, 31), new(2026, 6, 1), "ACC-AMEND"),
            Filing("13F-HR", new(2026, 3, 31), new(2026, 5, 13), "ACC-ORIG"),
        };

        var result = Holdings13FReconciliationWorker.SelectFilingsToReingest(
            edgar,
            new HashSet<DateOnly>(),
            MinReportDate,
            GlobalLatest
        );

        result.Select(f => f.AccessionNumber).Should().Equal("ACC-ORIG", "ACC-AMEND");
    }

    // A future-dated quarter past the global latest (a stray/erroneous report
    // date) must not drag the worker into ingesting beyond the ranking horizon.
    [Fact]
    public void SelectFilingsToReingest_ExcludesQuartersBeyondGlobalLatest()
    {
        var edgar = new[] { Filing("13F-HR", new(2026, 6, 30), new(2026, 8, 12), "ACC-FUTURE") };

        var result = Holdings13FReconciliationWorker.SelectFilingsToReingest(
            edgar,
            new HashSet<DateOnly>(),
            MinReportDate,
            GlobalLatest
        );

        result.Should().BeEmpty();
    }
}
