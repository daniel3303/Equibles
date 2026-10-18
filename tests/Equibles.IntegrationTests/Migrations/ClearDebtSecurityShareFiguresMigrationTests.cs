using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class ClearDebtSecurityShareFiguresMigrationTests : ParadeDbMcpTestBase
{
    public ClearDebtSecurityShareFiguresMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Up_ClearsOnlyDebtSecurities()
    {
        // A note carrying the common's figures is cleared; a listed preferred keeps its own pair.
        var note = Seed("TWOD", ListedSecurityType.DebtSecurities, 105_046_333, 2_722_800_951d);
        var preferred = Seed("GGB", ListedSecurityType.PreferredShares, 1_961_737_637, 9.8e9);
        var common = Seed("TWO", ListedSecurityType.CommonShares, 105_046_333, 2_667_126_394d);
        DbContext.AddRange(note, preferred, common);
        await DbContext.SaveChangesAsync();

        var commands = DbContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(new ClearDebtSecurityShareFigures().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);

        var stored = await DbContext
            .Set<EquitySecurity>()
            .AsNoTracking()
            .ToDictionaryAsync(s => s.EquityIssuerId, s => (s.SharesOutstanding, s.MarketCapitalization));
        stored[note.Id].Should().Be((0L, 0d));
        stored[preferred.Id].Should().Be((1_961_737_637L, 9.8e9));
        stored[common.Id].Should().Be((105_046_333L, 2_667_126_394d));
    }

    private static EquityIssuer Seed(
        string ticker,
        ListedSecurityType type,
        long shares,
        double marketCap
    ) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            ListedSecurityType: type,
            SharesOutStanding: shares,
            MarketCapitalization: marketCap
        );
}
