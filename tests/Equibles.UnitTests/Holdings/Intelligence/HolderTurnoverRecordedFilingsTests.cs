using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

/// <summary>
/// Replays two consecutive recorded 13F books (each filing's common-share rows as stored, plus the
/// stored daily-bar summaries the quarter average is computed from) through
/// <see cref="HolderQuarterMetricsBuilder"/>. Each issuer is its own sector, so the sector flow
/// rows read per issuer.
/// </summary>
public class HolderTurnoverRecordedFilingsTests
{
    private static readonly Guid Holder = Guid.NewGuid();

    [Fact]
    public void SwappedShareAndValueColumns_LeaveThoseTradesUnpriced()
    {
        // The 2021-12-31 filing put each position's dollar value in the share column and its share
        // count in the value column: 22 class A Berkshire shares read as 9,914,564 shares worth
        // $22,000, and the sales were priced at $4.87T against a $2.6B book.
        var replay = Replay("13f-trades-0001737088-2022-03-31.json");

        replay.Flow("084670108").Should().BeNull("both Berkshire classes were filed swapped");
        replay.Flow("023135106").Should().BeNull("3,811,150 Amazon shares were worth $1,143,000");
        replay.Metrics.PreviousEquityAum.Should().Be(2_626_417_662);
        Math.Round(replay.Metrics.SellValue.Value).Should().Be(2_094_838_268m);
        replay.Metrics.UnpricedTradeCount.Should().Be(532);
    }

    [Fact]
    public void ACorruptOneShareRow_DoesNotPriceThePriorQuartersShares()
    {
        // One share filed at $1,250,000 was the only price for the 1,000,000 shares held the
        // quarter before at $3,000 in all, a $1.25T sale.
        var replay = Replay("13f-trades-0001389709-2020-09-30.json");

        replay.Flow("86686Y105").Should().BeNull();
        Math.Round(replay.Metrics.SellValue.Value).Should().Be(132_433_123m);
        replay
            .Flow("084670108")
            .BuyValue.Should()
            .BeApproximately(97_886_087m, 1m, "319 class A shares bought is a real trade");
    }

    [Fact]
    public void ClassBSharesFiledUnderTheClassACusip_LeaveTheTradeUnpriced()
    {
        // 2,240 shares reported at $400,000 are class B shares filed under the class A CUSIP,
        // which the class A quarter average priced at $614M.
        var replay = Replay("13f-trades-0001724517-2020-06-30.json");

        replay.Flow("084670108").Should().BeNull();
        Math.Round(replay.Metrics.BuyValue.Value).Should().Be(120_007_615m);
    }

    private static RecordedReplay Replay(string fileName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Holdings",
                    "Intelligence",
                    fileName
                )
            )
        );
        var root = document.RootElement;
        var reportDate = DateOnly.Parse(root.GetProperty("reportDate").GetString());
        var rows = root.GetProperty("rows")
            .EnumerateArray()
            .Select(row => new HolderIntelligenceRow
            {
                InstitutionalHolderId = Holder,
                EquityIssuerId = Issuer(row[0].GetString()),
                ListedTicker = row[1].GetString(),
                ReportDate = DateOnly.Parse(row[2].GetString()),
                ShareType = ShareType.Shares,
                Shares = row[3].GetInt64(),
                Value = row[4].GetInt64(),
                ValueMissing = row[5].GetBoolean(),
                FiledValue = row[6].ValueKind == JsonValueKind.Null ? null : row[6].GetInt64(),
            })
            .ToList();
        var averagePrices = new Dictionary<HolderSecurityKey, decimal>();
        foreach (var price in root.GetProperty("prices").EnumerateArray())
        {
            var average = QuarterAveragePriceCalculator.AveragePrice(
                new QuarterPriceSeries
                {
                    AverageClose = price[2].GetDecimal(),
                    LastClose = price[3].GetDecimal(),
                    LastSessionDate = DateOnly.Parse(price[4].GetString()),
                    Sessions = price[5].GetInt32(),
                    ShareCountFactor =
                        price[6].ValueKind == JsonValueKind.Null ? null : price[6].GetDecimal(),
                    SplitInsideQuarter = price[7].GetBoolean(),
                    FiledPricePerShare = price[8].GetDecimal(),
                },
                reportDate
            );
            if (average != null)
                averagePrices[Security(price)] = average.Value;
        }
        var shareFactors = root.GetProperty("shareFactors")
            .EnumerateArray()
            .ToDictionary(
                Security,
                factor =>
                    factor[2].ValueKind == JsonValueKind.Null
                        ? (decimal?)null
                        : factor[2].GetDecimal()
            );

        var issuers = rows.Select(row => row.EquityIssuerId).Distinct().ToList();
        var result = HolderQuarterMetricsBuilder.Build(
            new HolderQuarterInput
            {
                InstitutionalHolderId = Holder,
                ReportDate = reportDate,
                Current = rows.Where(row => row.ReportDate == reportDate).ToList(),
                Previous = rows.Where(row => row.ReportDate != reportDate).ToList(),
                PreviousShareFactors = shareFactors,
                AveragePrices = averagePrices,
                IssuerSectors = issuers.ToDictionary(issuer => issuer, issuer => issuer),
            },
            HolderSectorFlowCalculator.FirstVersion
        );
        return new RecordedReplay(result.Metrics, result.SectorFlows);
    }

    private static HolderSecurityKey Security(JsonElement entry) =>
        new(Issuer(entry[0].GetString()), entry[1].GetString());

    // Fixture issuers are keyed by one of their CUSIPs.
    private static Guid Issuer(string cusip) => new(MD5.HashData(Encoding.UTF8.GetBytes(cusip)));

    private sealed record RecordedReplay(
        HolderQuarterlyMetrics Metrics,
        IReadOnlyList<HolderQuarterlySectorFlow> SectorFlows
    )
    {
        public HolderQuarterlySectorFlow Flow(string issuerCusip) =>
            SectorFlows.SingleOrDefault(flow => flow.SectorId == Issuer(issuerCusip));
    }
}
