using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.FunctionalTests.Fixtures;
using Equibles.Holdings.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Yahoo.Data.Models;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.FunctionalTests.Tests;

/// <summary>
/// End-to-end correctness tests for the Equibles MCP server. Each test seeds known data
/// directly into the Testcontainers ParadeDB through the fixture, then invokes a tool from
/// a different MCP module via the real MCP client SDK over Kestrel HTTP, and asserts that
/// the *response* carries the seeded values back to the client. Together this proves the
/// full pipeline — MCP client → HTTP → MCP framework → tool dispatch → DbContext query →
/// response framing → client — delivers correct results, not just non-empty placeholders.
///
/// Tools span every major data domain registered in <c>Program.ConfigureServices</c>:
/// Holdings, InsiderTrading, Congress, Fred (economic indicators), and StockPrices. A
/// regression in any of those module's MCP wiring surfaces here on the seeded substring
/// assertion — the existing direct-DbContext integration tests in
/// <c>Equibles.IntegrationTests/Mcp/*ToolsTests.cs</c> assert the same output shape but
/// bypass the MCP transport, so this is the only safety net for the wire-up path.
///
/// xUnit creates a fresh test class instance per <c>[Fact]</c>, and each test's
/// <c>InitializeAsync</c> calls <c>ResetAndSeedAsync</c> via Respawn so per-test data
/// isolation is automatic — tests can seed minimally for the tool they care about without
/// leaking state into siblings.
/// </summary>
[Trait("Category", "Functional")]
public class McpServerToolCorrectnessTests : IClassFixture<McpServerAppFixture>, IAsyncLifetime
{
    private readonly McpServerAppFixture _fixture;
    private McpClient _client;

    public McpServerToolCorrectnessTests(McpServerAppFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{_fixture.BaseUrl.TrimEnd('/')}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            }
        );
        _client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetTopHolders_SeededHoldingsForAapl_ResponseContainsHolderNamesAndShareCounts()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple Inc",
                Cik = "0000320193",
            };
            var berkshire = new InstitutionalHolder
            {
                Cik = "0001067983",
                Name = "Berkshire Hathaway",
                City = "Omaha",
                StateOrCountry = "NE",
            };
            var blackrock = new InstitutionalHolder
            {
                Cik = "0001166559",
                Name = "BlackRock Inc",
                City = "New York",
                StateOrCountry = "NY",
            };
            db.Set<CommonStock>().Add(stock);
            db.Set<InstitutionalHolder>().AddRange(berkshire, blackrock);
            await db.SaveChangesAsync();

            var reportDate = new DateOnly(2024, 3, 31);
            db.Set<InstitutionalHolding>()
                .AddRange(
                    BuildHolding(stock, berkshire, reportDate, shares: 10_000, value: 1_500_000),
                    BuildHolding(stock, blackrock, reportDate, shares: 5_000, value: 750_000)
                );
        });

        var tool = await GetTool("GetTopHolders");
        var text = await CallToolForText(tool, new() { ["ticker"] = "AAPL" });

        text.Should().Contain("Apple Inc");
        text.Should().Contain("AAPL");
        text.Should().Contain("Berkshire Hathaway");
        text.Should().Contain("BlackRock Inc");
        text.Should().Contain("10,000");
        text.Should().Contain("5,000");
        text.Should().Contain("2024-03-31");
    }

    [Fact]
    public async Task GetInsiderTransactions_SeededFormFourForCeo_ResponseContainsInsiderAndPriceAndShares()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "0000320193",
            };
            var owner = new InsiderOwner
            {
                OwnerCik = "0001214156",
                Name = "Tim Cook",
                City = "Cupertino",
                StateOrCountry = "CA",
                IsDirector = false,
                IsOfficer = true,
                OfficerTitle = "CEO",
            };
            db.Set<CommonStock>().Add(stock);
            db.Set<InsiderOwner>().Add(owner);
            await db.SaveChangesAsync();

            db.Set<InsiderTransaction>()
                .Add(
                    new InsiderTransaction
                    {
                        CommonStockId = stock.Id,
                        InsiderOwnerId = owner.Id,
                        TransactionDate = new DateOnly(2024, 3, 15),
                        FilingDate = new DateOnly(2024, 3, 17),
                        TransactionCode = TransactionCode.Sale,
                        Shares = 50_000,
                        PricePerShare = 175.50m,
                        AcquiredDisposed = AcquiredDisposed.Disposed,
                        SharesOwnedAfter = 200_000,
                        OwnershipNature = OwnershipNature.Direct,
                        SecurityTitle = "Common Stock",
                        AccessionNumber = "0001214156-24-000001",
                    }
                );
        });

        var tool = await GetTool("GetInsiderTransactions");
        var text = await CallToolForText(tool, new() { ["ticker"] = "AAPL" });

        text.Should().Contain("Apple Inc.");
        text.Should().Contain("AAPL");
        text.Should().Contain("Tim Cook");
        text.Should().Contain("CEO");
        text.Should().Contain("Sell");
        text.Should().Contain("2024-03-15");
        text.Should().Contain("50,000");
        text.Should().Contain("$175.50");
    }

    [Fact]
    public async Task GetCongressionalTrades_SeededTradeForRepresentative_ResponseShowsMemberNameAndAmountAndType()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Ticker = "NVDA",
                Name = "NVIDIA Corporation",
                Cik = "0001045810",
            };
            var member = new CongressMember
            {
                Name = "Nancy Pelosi",
                Position = CongressPosition.Representative,
            };
            db.Set<CommonStock>().Add(stock);
            db.Set<CongressMember>().Add(member);
            await db.SaveChangesAsync();

            db.Set<CongressionalTrade>()
                .Add(
                    new CongressionalTrade
                    {
                        CongressMemberId = member.Id,
                        CommonStockId = stock.Id,
                        TransactionDate = new DateOnly(2026, 3, 15),
                        FilingDate = new DateOnly(2026, 4, 14),
                        TransactionType = CongressTransactionType.Purchase,
                        OwnerType = "Self",
                        AssetName = "Common Stock",
                        AmountFrom = 1_000_001,
                        AmountTo = 5_000_000,
                    }
                );
        });

        var tool = await GetTool("GetCongressionalTrades");
        var text = await CallToolForText(
            tool,
            new()
            {
                ["ticker"] = "NVDA",
                ["startDate"] = "2026-01-01",
                ["endDate"] = "2026-04-30",
            }
        );

        text.Should().Contain("NVDA");
        text.Should().Contain("NVIDIA Corporation");
        text.Should().Contain("Nancy Pelosi");
        text.Should().Contain("Representative");
        text.Should().Contain("Purchase");
        text.Should().Contain("$1,000,001");
    }

    [Fact]
    public async Task GetEconomicIndicator_SeededFedFundsSeries_ResponseContainsSeriesTitleAndObservedValues()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var series = new FredSeries
            {
                SeriesId = "FEDFUNDS",
                Title = "Federal Funds Effective Rate",
                Category = FredSeriesCategory.InterestRates,
                Frequency = "Monthly",
                Units = "Percent",
                SeasonalAdjustment = "Not Seasonally Adjusted",
            };
            db.Set<FredSeries>().Add(series);
            await db.SaveChangesAsync();

            db.Set<FredObservation>()
                .AddRange(
                    new FredObservation
                    {
                        FredSeriesId = series.Id,
                        Date = new DateOnly(2025, 1, 1),
                        Value = 4.33m,
                    },
                    new FredObservation
                    {
                        FredSeriesId = series.Id,
                        Date = new DateOnly(2025, 2, 1),
                        Value = 4.33m,
                    },
                    new FredObservation
                    {
                        FredSeriesId = series.Id,
                        Date = new DateOnly(2025, 3, 1),
                        Value = 4.50m,
                    }
                );
        });

        var tool = await GetTool("GetEconomicIndicator");
        var text = await CallToolForText(
            tool,
            new()
            {
                ["seriesId"] = "FEDFUNDS",
                ["startDate"] = "2025-01-01",
                ["endDate"] = "2025-12-31",
            }
        );

        text.Should().Contain("Federal Funds Effective Rate (FEDFUNDS)");
        text.Should().Contain("Units: Percent");
        text.Should().Contain("Frequency: Monthly");
        text.Should().Contain("2025-01-01");
        text.Should().Contain("2025-03-01");
        text.Should().Contain(4.33m.ToString("G"));
        text.Should().Contain(4.50m.ToString("G"));
    }

    [Fact]
    public async Task GetStockPrices_SeededDailyOhlcvForAapl_ResponseContainsClosePricesAndVolumeAscending()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var stock = new CommonStock
            {
                Ticker = "AAPL",
                Name = "Apple Inc",
                Cik = "0000320193",
            };
            db.Set<CommonStock>().Add(stock);
            await db.SaveChangesAsync();

            db.Set<DailyStockPrice>()
                .AddRange(
                    BuildPrice(stock, new DateOnly(2026, 4, 1), close: 175.50m, volume: 50_000_000),
                    BuildPrice(stock, new DateOnly(2026, 4, 2), close: 176.25m, volume: 45_000_000)
                );
        });

        var tool = await GetTool("GetStockPrices");
        var text = await CallToolForText(
            tool,
            new()
            {
                ["ticker"] = "AAPL",
                ["startDate"] = "2026-03-01",
                ["endDate"] = "2026-04-30",
            }
        );

        text.Should().Contain("Daily prices for AAPL (Apple Inc)");
        text.Should().Contain("2026-04-01");
        text.Should().Contain("2026-04-02");
        text.Should().Contain("175.50");
        text.Should().Contain("176.25");
        text.Should().Contain("50,000,000");
        text.IndexOf("2026-04-01", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                text.IndexOf("2026-04-02", StringComparison.Ordinal),
                "OHLCV rows render ascending by date"
            );
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<McpClientTool> GetTool(string name)
    {
        var tools = await _client.ListToolsAsync();
        return tools.First(t => t.Name == name);
    }

    private static async Task<string> CallToolForText(
        McpClientTool tool,
        Dictionary<string, object> arguments
    )
    {
        var result = await tool.CallAsync(arguments);
        result.IsError.Should().NotBe(true);
        var textBlock = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        textBlock
            .Should()
            .NotBeNull(
                "every Equibles MCP tool returns its formatted output as a single TextContentBlock"
            );
        return textBlock.Text;
    }

    private static InstitutionalHolding BuildHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            CommonStock = stock,
            InstitutionalHolderId = holder.Id,
            InstitutionalHolder = holder,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(45),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"0001067983-24-{Guid.NewGuid().ToString()[..6]}",
            TitleOfClass = "COM",
            Cusip = "037833100",
        };

    private static DailyStockPrice BuildPrice(
        CommonStock stock,
        DateOnly date,
        decimal close,
        long volume
    ) =>
        new()
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            Date = date,
            Open = close - 1m,
            High = close + 1m,
            Low = close - 2m,
            Close = close,
            AdjustedClose = close,
            Volume = volume,
        };
}
