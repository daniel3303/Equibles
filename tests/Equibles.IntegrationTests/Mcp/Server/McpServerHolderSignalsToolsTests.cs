using System.Globalization;
using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.IntegrationTests.Mcp.Server;

/// <summary>
/// The holder-signal tools and the signal columns of the 13F tools read through readers the
/// production MCP server must register; a missing registration fails only when a tool is called.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class McpServerHolderSignalsToolsTests : IAsyncLifetime
{
    private static readonly DateOnly Quarter = new(2026, 3, 31);

    private readonly ParadeDbFixture _paradeDb;
    private readonly CultureInfo _previousCulture;
    private McpServerFixture _serverFixture;

    public McpServerHolderSignalsToolsTests(ParadeDbFixture paradeDb)
    {
        _paradeDb = paradeDb;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public async Task InitializeAsync()
    {
        await _paradeDb.ResetAsync();
        await using (var ctx = _paradeDb.CreateDbContext())
        {
            var nvidia = Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "NVDA",
                Name: "NVIDIA Corp",
                Cik: "0001045810",
                Cusip: "67066G104"
            );
            var amd = Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "AMD",
                Name: "Advanced Micro Devices",
                Cik: "0000002488",
                Cusip: "007903107"
            );
            var holder = new InstitutionalHolder { Cik = "315066", Name = "FMR LLC" };
            var target = new InstitutionalHolder { Cik = "1000001", Name = "Peer Only Capital" };
            ctx.AddRange(nvidia, amd, holder, target);
            ctx.AddRange(
                Holding(nvidia, holder, 1_000_000, 100_000_000),
                Holding(amd, target, 500_000, 60_000_000),
                Metrics(holder, 400_000_000),
                Metrics(target, 80_000_000),
                new HolderStockPresence
                {
                    InstitutionalHolderId = holder.Id,
                    EquityIssuerId = nvidia.Id,
                    HeldMask = HolderTenureCalculator.Set(
                        0,
                        HolderTenureCalculator.QuarterIndex(Quarter).Value
                    ),
                },
                new HolderIntelligenceQuarterState
                {
                    ReportDate = Quarter,
                    ComputedAt = new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc),
                    CalculationVersion = HolderCostBasisCalculator.FirstVersion,
                },
                new StockQuarterlyHolderSignals
                {
                    EquityIssuerId = nvidia.Id,
                    ReportDate = Quarter,
                    Filers = 1,
                    Shares = 1_000_000,
                    Value = 100_000_000,
                    ValuedShares = 1_000_000,
                    ConvictionHolders = 1,
                    SharesOutstanding = 24_000_000_000,
                    SharesOutstandingBasis = SharesOutstandingBasis.Consolidated,
                },
                new StockQuarterlyHolderOverlap
                {
                    EquityIssuerId = nvidia.Id,
                    ReportDate = Quarter,
                    Rank = 1,
                    PeerEquityIssuerId = amd.Id,
                    SharedHolders = 3,
                    Similarity = 0.75m,
                }
            );
            await ctx.SaveChangesAsync();
        }

        _serverFixture = new McpServerFixture(_paradeDb);
        await _serverFixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_serverFixture is not null)
            await ((IAsyncLifetime)_serverFixture).DisposeAsync();
        CultureInfo.CurrentCulture = _previousCulture;
    }

    [Theory]
    [InlineData("GetOwnershipSignals", "ticker", "NVDA", "Advanced Micro Devices")]
    [InlineData("GetHolderTargets", "ticker", "NVDA", "Peer Only Capital")]
    [InlineData("GetMostHeldStocks", "sort", "conviction", "NVIDIA Corp")]
    [InlineData("GetInstitutionPortfolio", "institutionName", "315066", "Held (qtrs)")]
    public async Task CallTool_ResolvesThroughTheServersComposition(
        string tool,
        string argument,
        string value,
        string expected
    )
    {
        var httpClient = _serverFixture.CreateClient();
        httpClient.BaseAddress = new Uri("http://localhost/");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient
        );
        await using var client = await McpClient.CreateAsync(transport);

        var result = await client.CallToolAsync(
            toolName: tool,
            arguments: new Dictionary<string, object> { [argument] = value }
        );

        result.IsError.Should().NotBe(true);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        text.Should().Contain(expected);
    }

    private static InstitutionalHolding Holding(
        Equibles.CommonStocks.Data.Models.EquityIssuer issuer,
        InstitutionalHolder holder,
        long shares,
        long value
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = Quarter,
            FilingDate = new DateOnly(2026, 5, 10),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            FilingType = FilingType.Form13F,
            AccessionNumber = $"13f-{holder.Cik}-20260331",
        };

    private static HolderQuarterlyMetrics Metrics(InstitutionalHolder holder, long aum) =>
        new()
        {
            InstitutionalHolderId = holder.Id,
            ReportDate = Quarter,
            EquityAum = aum,
            StyleBand = HolderStyleBand.Concentrated,
            CalculationVersion = HolderCostBasisCalculator.FirstVersion,
        };
}
