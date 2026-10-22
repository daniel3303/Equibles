using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the filed-holder test to one probe per holder. Selecting the ids off every 13F position
/// read the requested holders' whole books for the quarter: 2.3M index rows and 16 s for Apple's
/// holders, where an EXISTS probe reads about one row per holder.
/// </summary>
public class InstitutionalHoldingRepositoryFiledHolderQueryTests
{
    [Fact]
    public void GetFiledHolderIdsAmong_ProbesEachHolderWithExists()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        using var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
        var repository = new InstitutionalHoldingRepository(context);

        var sql = repository
            .GetFiledHolderIdsAmong(new DateOnly(2026, 6, 30), [Guid.NewGuid(), Guid.NewGuid()])
            .ToQueryString();

        sql.Should().Contain("FROM \"InstitutionalHolder\" AS");
        var probe = Regex.Match(
            sql,
            "EXISTS \\(\\s*SELECT 1\\s+FROM \"InstitutionalHolding\" AS \\w+\\s+WHERE ([^)]*)\\)"
        );
        probe.Success.Should().BeTrue();
        probe.Groups[1].Value.Should().MatchRegex("\"InstitutionalHolderId\" = \\w+\\.\"Id\"");
        probe.Groups[1].Value.Should().NotContain("ANY");
        sql.Should().Contain("UNION");
        sql.Should().Contain("\"IsAmendment\"");
    }
}
