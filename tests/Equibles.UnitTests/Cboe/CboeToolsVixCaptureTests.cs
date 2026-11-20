using System.Threading.Tasks;
using Equibles.Cboe.Data;
using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Mcp.Captures;
using Equibles.Cboe.Mcp.Tools;
using Equibles.Cboe.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Cboe;

// GetVixHistory hands a host the exact rows its answer lists, newest kept and oldest first, and
// nothing when the range is empty, so a card beside the answer can never show more or other rows.
public class CboeToolsVixCaptureTests
{
    private static readonly InMemoryDatabaseRoot Root = new();

    [Fact]
    public async Task GetVixHistory_RecordsTheRowsItListed()
    {
        await using var ctx = NewContext();
        ctx.Set<CboeVixDaily>()
            .AddRange(
                Day(new DateOnly(2026, 11, 16), 22.91m),
                Day(new DateOnly(2026, 11, 17), 25.46m),
                Day(new DateOnly(2026, 11, 18), 24.56m)
            );
        await ctx.SaveChangesAsync();

        using var scope = McpResultCapture.Begin();
        var text = await NewTools(ctx).GetVixHistory("2026-11-01", "2026-11-30", maxResults: 2);

        var capture = scope.Get<VixHistoryCapture>();
        Assert.NotNull(capture);
        Assert.Equal(
            [new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 18)],
            capture.Rows.Select(row => row.Date)
        );
        Assert.Equal(3, capture.TotalInRange);
        Assert.Equal(new DateOnly(2026, 11, 1), capture.Start);
        Assert.Contains("25.46", text);
    }

    [Fact]
    public async Task GetVixHistory_EmptyRange_RecordsNothing()
    {
        await using var ctx = NewContext();

        using var scope = McpResultCapture.Begin();
        await NewTools(ctx).GetVixHistory("2026-11-01", "2026-11-30");

        Assert.Null(scope.Get<VixHistoryCapture>());
    }

    private static CboeVixDaily Day(DateOnly date, decimal close) =>
        new()
        {
            Date = date,
            Open = close,
            High = close + 1,
            Low = close - 1,
            Close = close,
        };

    private static CboeTools NewTools(EquiblesFinancialDbContext ctx) =>
        new(
            new CboePutCallRatioRepository(ctx),
            new CboeVixDailyRepository(ctx),
            new ErrorManager(null),
            NullLogger<CboeTools>.Instance
        );

    private static EquiblesFinancialDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), Root)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CboeModuleConfiguration() }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
