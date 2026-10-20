using System.Globalization;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings.Ownership;

public class StockOwnershipCacheKeyTests
{
    [Fact]
    public void CacheKey_IsTheSameUnderEveryCulture()
    {
        var stockId = Guid.Parse("8f1b6c33-7c5e-4a55-9a43-0c6f2f0a1d11");
        var anchor = new StockQuarterAnchor
        {
            ReportDate = new DateOnly(2026, 6, 30),
            PreviousReportDate = new DateOnly(2026, 3, 31),
            FilingWindowOpen = true,
        };
        var snapshot = new HoldingsSnapshotState(
            false,
            new DateTime(2026, 8, 14, 3, 4, 5, DateTimeKind.Utc)
        );
        var links = new DateTime(2026, 8, 13, 22, 0, 0, DateTimeKind.Utc);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var keys = new[] { "en-US", "de-DE", "ar-SA", "fa-IR", "" }
                .Select(name =>
                {
                    CultureInfo.CurrentCulture = new CultureInfo(name);
                    return StockOwnershipReader.CacheKey(stockId, "NVDA", anchor, snapshot, links);
                })
                .ToList();

            keys.Distinct().Should().ContainSingle();
            keys[0].Should().Contain("2026-06-30").And.Contain("2026-08-14T03:04:05");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void CacheKey_ChangesWhenTheLinksOrSnapshotRefresh()
    {
        var anchor = new StockQuarterAnchor { ReportDate = new DateOnly(2026, 6, 30) };
        var stockId = Guid.NewGuid();
        var snapshot = new HoldingsSnapshotState(false, new DateTime(2026, 8, 14));

        var key = StockOwnershipReader.CacheKey(
            stockId,
            "NVDA",
            anchor,
            snapshot,
            new DateTime(2026, 8, 1)
        );

        key.Should()
            .NotBe(
                StockOwnershipReader.CacheKey(
                    stockId,
                    "NVDA",
                    anchor,
                    snapshot,
                    new DateTime(2026, 8, 2)
                )
            );
        key.Should()
            .NotBe(
                StockOwnershipReader.CacheKey(
                    stockId,
                    "NVDA",
                    anchor,
                    snapshot with
                    {
                        ComputedAt = new DateTime(2026, 8, 15),
                    },
                    new DateTime(2026, 8, 1)
                )
            );
        key.Should()
            .NotBe(
                StockOwnershipReader.CacheKey(
                    stockId,
                    "NVDA-B",
                    anchor,
                    snapshot,
                    new DateTime(2026, 8, 1)
                )
            );
    }
}
