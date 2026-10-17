using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService;
using Equibles.Sec.FinancialFacts.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The version-9 replay re-reads only the documents a Company Facts gap can still affect: 10-Q/10-K
/// filings filed since the replay window opened or newer than their issuer's Company Facts
/// watermark. Everything else stamped at the floor stays put instead of re-draining the corpus.
/// </summary>
public class XbrlFactsExtractionWorkerSelectDueDocumentsTests
{
    private static readonly Guid IssuerId = Guid.NewGuid();

    [Theory]
    [InlineData(0, "EightK", "2020-01-01", true)]
    [InlineData(XbrlFactExtractionService.TargetedReplayFloor, "TenQ", "2026-07-29", true)]
    [InlineData(XbrlFactExtractionService.TargetedReplayFloor, "TenKa", "2026-02-24", true)]
    [InlineData(XbrlFactExtractionService.TargetedReplayFloor, "TenQ", "2025-10-29", false)]
    [InlineData(XbrlFactExtractionService.TargetedReplayFloor, "TenK", "2025-12-01", true)]
    [InlineData(XbrlFactExtractionService.TargetedReplayFloor, "EightK", "2026-07-29", false)]
    [InlineData(XbrlFactExtractionService.TargetedReplayFloor, "SixK", "2026-07-29", false)]
    [InlineData(XbrlFactExtractionService.CurrentVersion, "TenQ", "2026-07-29", false)]
    public void SelectDueDocuments_PicksOnlyTheTargetedReplay(
        int version,
        string form,
        string filed,
        bool due
    )
    {
        // Company Facts has read everything filed up to 2025-11-15 for this issuer.
        var checkpoints = new[]
        {
            new FinancialFactsSyncStatus
            {
                EquityIssuerId = IssuerId,
                LastFiledDateSeen = new DateOnly(2025, 11, 15),
            },
        };

        XbrlFactsExtractionWorker
            .SelectDueDocuments(
                new[] { Filing(version, form, filed) }.AsQueryable(),
                checkpoints.AsQueryable()
            )
            .Should()
            .HaveCount(due ? 1 : 0);
    }

    [Fact]
    public void SelectDueDocuments_IssuerWithoutCompanyFactsCheckpoint_ReplaysOlderPeriodicFilings()
    {
        XbrlFactsExtractionWorker
            .SelectDueDocuments(
                new[]
                {
                    Filing(XbrlFactExtractionService.TargetedReplayFloor, "TenQ", "2024-05-01"),
                }.AsQueryable(),
                Array.Empty<FinancialFactsSyncStatus>().AsQueryable()
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void SelectDueDocuments_ExhaustedAttempts_AreNotReplayed()
    {
        var document = Filing(XbrlFactExtractionService.TargetedReplayFloor, "TenQ", "2026-07-29");
        document.XbrlFactsAttempts = Document.MaxXbrlFactsAttempts;

        XbrlFactsExtractionWorker
            .SelectDueDocuments(
                new[] { document }.AsQueryable(),
                Array.Empty<FinancialFactsSyncStatus>().AsQueryable()
            )
            .Should()
            .BeEmpty();
    }

    private static Document Filing(int version, string form, string filed) =>
        new()
        {
            EquityIssuerId = IssuerId,
            DocumentType = DocumentType.FromValue(form),
            ReportingDate = DateOnly.Parse(filed),
            XbrlFactsVersion = version,
        };
}
