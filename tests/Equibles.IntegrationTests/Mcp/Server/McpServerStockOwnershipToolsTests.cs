using System.Globalization;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Equibles.IntegrationTests.Mcp.Server;

/// <summary>
/// The ownership tools need the unified holder list's reader and every lane registered in the
/// production MCP server; a missing registration fails only when a tool is first called.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class McpServerStockOwnershipToolsTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _paradeDb;
    private readonly CultureInfo _previousCulture;
    private McpServerFixture _serverFixture;

    public McpServerStockOwnershipToolsTests(ParadeDbFixture paradeDb)
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
            var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "NVDA",
                Name: "NVIDIA Corp",
                Cik: "0001045810",
                Cusip: "67066G104"
            );
            var holder = new InstitutionalHolder { Cik = "315066", Name = "FMR LLC" };
            ctx.AddRange(stock, holder);
            ctx.Add(
                new InstitutionalHolding
                {
                    EquityIssuerId = stock.Id,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = new DateOnly(2026, 3, 31),
                    FilingDate = new DateOnly(2026, 5, 10),
                    Shares = 1_234_567,
                    Value = 100_000_000,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    FilingType = FilingType.Form13F,
                    AccessionNumber = "13f-315066-20260331",
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
    [InlineData("GetAllHolders", "FMR LLC")]
    [InlineData("GetOwnershipSummary", "Institutional (13F)")]
    [InlineData("GetBeneficialOwnership", "No Schedule 13D or 13G filings found")]
    public async Task CallTool_ResolvesThroughTheServersComposition(string tool, string expected)
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
            arguments: new Dictionary<string, object> { ["ticker"] = "NVDA" }
        );

        result.IsError.Should().NotBe(true);
        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        text.Should().Contain(expected);
    }
}
