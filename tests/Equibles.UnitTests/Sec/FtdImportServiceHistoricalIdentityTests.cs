using Equibles.Sec.HostedService.Services;
using Xunit;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceHistoricalIdentityTests
{
    [Fact]
    public void Resolve_RecycledTicker_UsesEarliestCutoffCoveringSettlementDate()
    {
        var oldId = Guid.NewGuid();
        var laterId = Guid.NewGuid();
        var map = new Dictionary<string, List<FtdImportService.HistoricalTickerIdentity>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["USED"] =
            [
                new(oldId, "USED", new DateOnly(2020, 6, 30)),
                new(laterId, "USED", new DateOnly(2024, 6, 30)),
            ],
        };

        var resolved = FtdImportService.TryResolveHistoricalIdentity(
            "USED",
            new DateOnly(2020, 6, 30),
            map,
            [],
            out var stockId
        );

        resolved.Should().BeTrue();
        stockId.Should().Be(oldId);
    }

    [Fact]
    public void Resolve_EqualCoveringCutoffs_RefusesAmbiguousIdentity()
    {
        var cutoff = new DateOnly(2020, 6, 30);
        var map = new Dictionary<string, List<FtdImportService.HistoricalTickerIdentity>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["USED"] = [new(Guid.NewGuid(), "USED", cutoff), new(Guid.NewGuid(), "USED", cutoff)],
        };

        var resolved = FtdImportService.TryResolveHistoricalIdentity(
            "USED",
            cutoff,
            map,
            [],
            out _
        );

        resolved.Should().BeFalse();
    }

    [Fact]
    public void Resolve_AfterInclusiveCutoff_RefusesIdentity()
    {
        var map = new Dictionary<string, List<FtdImportService.HistoricalTickerIdentity>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["GONE"] = [new(Guid.NewGuid(), "GONE", new DateOnly(2020, 6, 30))],
        };

        var resolved = FtdImportService.TryResolveHistoricalIdentity(
            "GONE",
            new DateOnly(2020, 7, 1),
            map,
            [],
            out _
        );

        resolved.Should().BeFalse();
    }

    [Fact]
    public void SelectCusips_DifferentValuesOnLatestDate_DropsStock()
    {
        var stockId = Guid.NewGuid();
        var stock = new Equibles.CommonStocks.Data.Models.CommonStock { Id = stockId };
        FtdImportService.ApplyHistoricalCusipEvidence(
            stock,
            [new(stockId, "111111111", new DateOnly(2020, 6, 30))]
        );
        FtdImportService.ApplyHistoricalCusipEvidence(
            stock,
            [new(stockId, "222222222", new DateOnly(2020, 6, 30))]
        );
        FtdImportService.ApplyHistoricalCusipEvidence(
            stock,
            [new(stockId, "000000000", new DateOnly(2020, 6, 29))]
        );

        stock.HistoricalCusipBackfillCandidates.Should().Equal("111111111", "222222222");
        stock.HistoricalCusipBackfillAmbiguous.Should().BeTrue();
        stock.HistoricalCusipBackfillCandidateOn.Should().Be(new DateOnly(2020, 6, 30));
    }

    [Fact]
    public void SelectCusips_SameValueClaimsMultipleStocks_DropsEveryOwner()
    {
        var stocks = new[]
        {
            new Equibles.CommonStocks.Data.Models.CommonStock
            {
                HistoricalCusipBackfillCandidates = ["111111111"],
            },
            new Equibles.CommonStocks.Data.Models.CommonStock
            {
                HistoricalCusipBackfillCandidates = ["111111111"],
            },
        };

        FtdImportService.RejectContestedHistoricalCusips(stocks);

        stocks
            .Should()
            .OnlyContain(stock =>
                stock.HistoricalCusipBackfillCandidates.Count == 1
                && stock.HistoricalCusipBackfillCandidates[0] == "111111111"
            );
        stocks.Should().OnlyContain(stock => stock.HistoricalCusipBackfillAmbiguous);
    }

    [Fact]
    public void SelectCusips_ContestedClaimFromEarlierBatch_RemainsVisibleToLaterOwner()
    {
        var first = new Equibles.CommonStocks.Data.Models.CommonStock
        {
            HistoricalCusipBackfillCandidates = ["111111111"],
        };
        var second = new Equibles.CommonStocks.Data.Models.CommonStock
        {
            HistoricalCusipBackfillCandidates = ["111111111"],
        };
        FtdImportService.RejectContestedHistoricalCusips([first, second]);

        var later = new Equibles.CommonStocks.Data.Models.CommonStock
        {
            HistoricalCusipBackfillCandidates = ["111111111"],
        };
        FtdImportService.RejectContestedHistoricalCusips([first, second, later]);

        new[] { first, second, later }
            .Should()
            .OnlyContain(stock => stock.HistoricalCusipBackfillAmbiguous);
    }
}
