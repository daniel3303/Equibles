using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Pins the AUM ($ value) and position-count range filters on the /institutions
/// index. Both bound each filer's most-recent 13F aggregate; a positive lower
/// bound also excludes filers that have never reported a 13F (treated as zero).
/// </summary>
[Collection(WebHostCollection.Name)]
public class InstitutionsControllerRangeFilterTests
{
    private static readonly DateOnly Quarter = new(2024, 12, 31);

    private readonly WebHostFixture _fixture;

    public InstitutionsControllerRangeFilterTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetIndex_MinPositions_ExcludesFilersBelowThreshold()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var bigId = Guid.NewGuid();
        var smallId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(MakeStock(aaplId, "AAPL", "0000320193"));
            db.Add(MakeStock(msftId, "MSFT", "0000789019"));
            db.Add(
                new InstitutionalHolder
                {
                    Id = bigId,
                    Cik = "0000010",
                    Name = "Big Fund LP",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = smallId,
                    Cik = "0000020",
                    Name = "Small Boutique LLC",
                }
            );
            // Big Fund holds two positions; Small Boutique holds one.
            db.Add(MakeHolding(aaplId, bigId, 1_000_000));
            db.Add(MakeHolding(msftId, bigId, 1_000_000));
            db.Add(MakeHolding(aaplId, smallId, 1_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions?minPositions=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Big Fund LP");
        html.Should().NotContain("Small Boutique LLC");
    }

    [Fact]
    public async Task GetIndex_MaxPositions_ExcludesFilersAboveThreshold()
    {
        var aaplId = Guid.NewGuid();
        var msftId = Guid.NewGuid();
        var bigId = Guid.NewGuid();
        var smallId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(MakeStock(aaplId, "AAPL", "0000320193"));
            db.Add(MakeStock(msftId, "MSFT", "0000789019"));
            db.Add(
                new InstitutionalHolder
                {
                    Id = bigId,
                    Cik = "0000010",
                    Name = "Big Fund LP",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = smallId,
                    Cik = "0000020",
                    Name = "Small Boutique LLC",
                }
            );
            db.Add(MakeHolding(aaplId, bigId, 1_000_000));
            db.Add(MakeHolding(msftId, bigId, 1_000_000));
            db.Add(MakeHolding(aaplId, smallId, 1_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions?maxPositions=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Small Boutique LLC");
        html.Should().NotContain("Big Fund LP");
    }

    [Fact]
    public async Task GetIndex_MinValue_KeepsOnlyFilersWithBookSizeAtOrAboveBound()
    {
        var aaplId = Guid.NewGuid();
        var whaleId = Guid.NewGuid();
        var minnowId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(MakeStock(aaplId, "AAPL", "0000320193"));
            db.Add(
                new InstitutionalHolder
                {
                    Id = whaleId,
                    Cik = "0000050",
                    Name = "Whale Fund LP",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = minnowId,
                    Cik = "0000060",
                    Name = "Minnow Fund LP",
                }
            );
            db.Add(MakeHolding(aaplId, whaleId, 100_000_000));
            db.Add(MakeHolding(aaplId, minnowId, 1_000));
            await Task.CompletedTask;
        });

        // Lower bound of $1M keeps the whale ($100M) and drops the minnow ($1k).
        var response = await _fixture.Client.GetAsync("/institutions?minValue=1000000");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Whale Fund LP");
        html.Should().NotContain("Minnow Fund LP");
    }

    [Fact]
    public async Task GetIndex_ValueRange_BoundsBothEnds()
    {
        var aaplId = Guid.NewGuid();
        var whaleId = Guid.NewGuid();
        var midId = Guid.NewGuid();
        var minnowId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(MakeStock(aaplId, "AAPL", "0000320193"));
            db.Add(
                new InstitutionalHolder
                {
                    Id = whaleId,
                    Cik = "0000050",
                    Name = "Whale Fund LP",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = midId,
                    Cik = "0000055",
                    Name = "Middle Fund LP",
                }
            );
            db.Add(
                new InstitutionalHolder
                {
                    Id = minnowId,
                    Cik = "0000060",
                    Name = "Minnow Fund LP",
                }
            );
            db.Add(MakeHolding(aaplId, whaleId, 100_000_000));
            db.Add(MakeHolding(aaplId, midId, 5_000_000));
            db.Add(MakeHolding(aaplId, minnowId, 1_000));
            await Task.CompletedTask;
        });

        // $1M..$10M window keeps only the middle fund.
        var response = await _fixture.Client.GetAsync(
            "/institutions?minValue=1000000&maxValue=10000000"
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Middle Fund LP");
        html.Should().NotContain("Whale Fund LP");
        html.Should().NotContain("Minnow Fund LP");
    }

    [Fact]
    public async Task GetIndex_PositiveLowerBound_ExcludesNeverReportedFilers()
    {
        var aaplId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var dormantId = Guid.NewGuid();

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(MakeStock(aaplId, "AAPL", "0000320193"));
            db.Add(
                new InstitutionalHolder
                {
                    Id = activeId,
                    Cik = "0000070",
                    Name = "Active Fund LP",
                }
            );
            // Dormant filer with no holdings at all — aggregate is zero.
            db.Add(
                new InstitutionalHolder
                {
                    Id = dormantId,
                    Cik = "0000080",
                    Name = "Dormant Fund LP",
                }
            );
            db.Add(MakeHolding(aaplId, activeId, 1_000_000));
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/institutions?minPositions=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Active Fund LP");
        html.Should().NotContain("Dormant Fund LP");
    }

    private static CommonStock MakeStock(Guid id, string ticker, string cik) =>
        new()
        {
            Id = id,
            Ticker = ticker,
            Name = ticker,
            Cik = cik,
        };

    private static InstitutionalHolding MakeHolding(Guid stockId, Guid holderId, long value) =>
        new()
        {
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = Quarter,
            FilingDate = Quarter.AddDays(45),
            Shares = 1_000,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber =
                $"acc-{holderId:N}".Substring(0, 12) + $"-{stockId:N}".Substring(0, 8),
        };
}
