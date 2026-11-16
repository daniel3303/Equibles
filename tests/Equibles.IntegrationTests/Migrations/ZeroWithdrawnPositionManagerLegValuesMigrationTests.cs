using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Equibles.IntegrationTests.Migrations;

[Collection(ParadeDbCollection.Name)]
public class ZeroWithdrawnPositionManagerLegValuesMigrationTests : ParadeDbMcpTestBase
{
    public ZeroWithdrawnPositionManagerLegValuesMigrationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // A withdrawn position's legs lose their value; a valued position's legs keep theirs, and so
    // do a merged position's, which is unavailable but still carries a value.
    [Fact]
    public async Task Up_ZeroesOnlyTheLegsOfWithdrawnPositions()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NAAS",
            Name: "Issuer",
            Cik: "0001000001"
        );
        var holder = new InstitutionalHolder { Cik = "0002000002", Name = "Filer" };
        DbContext.AddRange(stock, holder);
        var withdrawn = Position(stock, holder, value: 0, unavailable: true, legValue: 100_800);
        var valued = Position(stock, holder, value: 500, unavailable: false, legValue: 500);
        valued.ShareType = ShareType.Principal;
        var merged = Position(stock, holder, value: 700, unavailable: true, legValue: 700);
        merged.FilingType = FilingType.Schedule13G;
        DbContext.AddRange(withdrawn, valued, merged);
        await DbContext.SaveChangesAsync();

        var commands = DbContext
            .GetService<IMigrationsSqlGenerator>()
            .Generate(new ZeroWithdrawnPositionManagerLegValues().UpOperations);
        foreach (var command in commands)
            await DbContext.Database.ExecuteSqlRawAsync(command.CommandText);

        var legs = await DbContext
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .ToDictionaryAsync(h => h.Id, h => h.ManagerEntries.Single().Value);
        legs[withdrawn.Id].Should().Be(0L);
        legs[valued.Id].Should().Be(500L);
        legs[merged.Id].Should().Be(700L);
    }

    private static InstitutionalHolding Position(
        EquityIssuer stock,
        InstitutionalHolder holder,
        long value,
        bool unavailable,
        long legValue
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = new DateOnly(2026, 3, 31),
            FilingDate = new DateOnly(2026, 5, 10),
            Shares = 32_098_694_296,
            Value = value,
            ValueUnavailable = unavailable,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
            ManagerEntries =
            [
                new HoldingManagerEntry
                {
                    ManagerNumber = 1,
                    ManagerName = "Leg",
                    Shares = 32_098_694_296,
                    Value = legValue,
                },
            ],
        };
}
