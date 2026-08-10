using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.GovernmentContracts.Data;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.Mcp.Tools;
using Equibles.GovernmentContracts.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.GovernmentContracts;

public class GovernmentContractsToolsShortenSurrogateTests
{
    // Contract: Shorten caps a display string at maxLength for an MCP markdown
    // cell (GetGovernmentContracts truncates each contract Description to 80).
    // Truncation must leave the result well-formed UTF-16 — the sibling
    // UsaSpendingAwardMapper.Truncate in this same module was fixed for exactly
    // this (GH-3786): slicing through a surrogate pair orphans a lone surrogate,
    // which is invalid UTF-16 and corrupts JSON serialization of the tool reply.
    // Here the cut lands between the two halves of "😀" (U+1F600), so a raw
    // value[..80] keeps the high half and drops the low half.
    //
    // Reflection-invoke since Shorten is private static.
    [Fact]
    public void Shorten_CutThroughSurrogatePair_DoesNotOrphanSurrogate()
    {
        var method = typeof(GovernmentContractsTools).GetMethod(
            "Shorten",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // 79 BMP chars place the high surrogate of "😀" at index 79, so a raw
        // value[..80] retains the high half and orphans it.
        var input = new string('a', 79) + "😀" + new string('b', 10);

        var result = (string)method!.Invoke(null, [input, 80]);

        HasUnpairedSurrogate(result)
            .Should()
            .BeFalse("truncation must not split a surrogate pair into a lone surrogate");
    }

    [Fact]
    public void Escape_BackslashBeforePipe_KeepsAwardCellInsideItsColumn()
    {
        var method = typeof(GovernmentContractsTools).GetMethod(
            "Escape",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (string)method!.Invoke(null, ["Subsidiary\\|Division"]);

        result.Should().Be("Subsidiary\\\\\\|Division");
    }

    [Fact]
    public async Task GetGovernmentContracts_RendersRecipientAndOutlaysWithSafeCells()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;
        await using var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new GovernmentContractsModuleConfiguration(),
            }
        );
        context.Database.EnsureCreated();
        var stock = new CommonStock
        {
            Ticker = "RTX",
            Name = "RTX Corp",
            Cik = "0000101829",
        };
        context.Add(stock);
        context.Add(
            new GovernmentContract
            {
                CommonStockId = stock.Id,
                AwardUniqueKey = "award-1",
                AwardId = "W58RGZ26C0001",
                RecipientName =
                    @"RTX Defense Holdings International Systems and\|Missiles Division LLC",
                AwardType = GovernmentContractAwardType.DefinitiveContract,
                AwardingAgency = "Department of Defense",
                Amount = 1_500_000m,
                TotalOutlays = 250_000m,
                ActionDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2028, 6, 1),
                Description = "Missile systems",
            }
        );
        await context.SaveChangesAsync();
        var tools = new GovernmentContractsTools(
            new GovernmentContractRepository(context),
            new CommonStockRepository(context),
            new ErrorManager(null!),
            NullLogger<GovernmentContractsTools>.Instance
        );

        var result = await tools.GetGovernmentContracts("RTX", "2026-01-01", "2026-12-31");

        result.Should().Contain("| Award Date | Recipient | Agency |");
        result.Should().Contain("| Outlays |");
        result
            .Should()
            .Contain(@"RTX Defense Holdings International Systems and\\\|Missiles Division LLC");
        result.Should().Contain("$250,000");

        var ranking = await tools.GetTopGovernmentContractors("2026-01-01", "2026-12-31");
        ranking
            .Should()
            .NotContain(
                "Recipient is the awarded entity",
                "the market-wide ranking has no Recipient column"
            );
    }

    private static bool HasUnpairedSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return true;
                i++;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return true;
            }
        }

        return false;
    }
}
