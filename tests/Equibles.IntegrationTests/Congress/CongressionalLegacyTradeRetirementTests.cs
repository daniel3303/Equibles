using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

/// <summary>
/// Rows stored before stable source identity stayed beside their source-keyed replay copies when
/// the old parser had missed metadata (an empty subholding, a "F S:" suffix in the name), so member
/// pages counted those trades twice. The retirement deletes a legacy row only when one replay row
/// of the same filing carries the same or more filed metadata.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressionalLegacyTradeRetirementTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly TransactionDate = new(2025, 1, 13);
    private static readonly DateOnly FilingDate = new(2025, 2, 13);

    public CongressionalLegacyTradeRetirementTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CongressionalLegacyTradeRetirement BuildSut() =>
        new(DbContext, NullLogger<CongressionalLegacyTradeRetirement>());

    private static CongressionalTrade Legacy(
        CongressMember member,
        string assetName = "UnitedHealth Group Incorporated Common Stock (UNH)",
        string assetType = "ST",
        string subholding = "",
        DateOnly? filingDate = null
    ) =>
        new()
        {
            CongressMemberId = member.Id,
            TransactionDate = TransactionDate,
            FilingDate = filingDate ?? FilingDate,
            TransactionType = CongressTransactionType.Sale,
            OwnerType = "JT",
            AssetName = assetName,
            AssetType = assetType,
            Subholding = subholding,
            AmountFrom = 1_001,
            AmountTo = 15_000,
        };

    private static CongressionalTrade Keyed(
        CongressMember member,
        int sourceRowIndex,
        string assetType = "ST",
        string subholding = "Joint Ownership LPL Account",
        string assetName = "UnitedHealth Group Incorporated Common Stock (UNH)"
    )
    {
        var trade = Legacy(member, assetName, assetType, subholding);
        trade.FiledTicker = "UNH";
        trade.FilingKind = CongressionalFilingKind.HousePeriodicTransactionReport;
        trade.SourceId = "20024346";
        trade.SourceRowIndex = sourceRowIndex;
        return trade;
    }

    private async Task<CongressMember> SeedMember()
    {
        var member = new CongressMember
        {
            Name = "Jane Doe",
            Position = CongressPosition.Representative,
        };
        DbContext.Add(member);
        await DbContext.SaveChangesAsync(CancellationToken.None);
        return member;
    }

    private async Task<List<CongressionalTrade>> Retire(params CongressionalTrade[] trades)
    {
        DbContext.AddRange(trades);
        await DbContext.SaveChangesAsync(CancellationToken.None);
        DbContext.ChangeTracker.Clear();

        await BuildSut().RetireSuperseded(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        return await verify
            .Set<CongressionalTrade>()
            .AsNoTracking()
            .ToListAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RetireSuperseded_ReplayAddsSubholdingAndDropsIssuerLink_DeletesLegacyRow()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "UNH",
            Name: "UnitedHealth Group Incorporated"
        );
        var member = await SeedMember();
        DbContext.Add(stock);
        var legacy = Legacy(member);
        legacy.Issuer = Equibles
            .TestSupport.NativeListingSeed.ForStock(DbContext, stock)
            .Security.Issuer;
        legacy.EquityIssuerId = stock.Id;

        var stored = await Retire(legacy, Keyed(member, 121));

        var trade = stored.Should().ContainSingle().Subject;
        trade.SourceId.Should().Be("20024346");
        trade.Subholding.Should().Be("Joint Ownership LPL Account");
    }

    [Fact]
    public async Task RetireSuperseded_LegacyNameCarriesFilingStatus_DeletesLegacyRow()
    {
        var member = await SeedMember();

        var stored = await Retire(
            Legacy(member, assetName: "Broadcom Inc. - Common Stock (AVGO) F S: New"),
            Keyed(member, 30, assetName: "Broadcom Inc. - Common Stock (AVGO)")
        );

        stored.Should().ContainSingle().Which.SourceId.Should().NotBeNull();
    }

    [Theory]
    [InlineData("OP", "")]
    [InlineData("ST", "Brokerage IRA")]
    public async Task RetireSuperseded_LegacyMetadataConflicts_KeepsBothRows(
        string legacyAssetType,
        string legacySubholding
    )
    {
        var member = await SeedMember();

        var stored = await Retire(
            Legacy(member, assetType: legacyAssetType, subholding: legacySubholding),
            Keyed(member, 121)
        );

        stored.Should().HaveCount(2);
    }

    [Fact]
    public async Task RetireSuperseded_OtherFilingDate_KeepsLegacyRow()
    {
        var member = await SeedMember();

        var stored = await Retire(
            Legacy(member, filingDate: FilingDate.AddDays(1)),
            Keyed(member, 121)
        );

        stored.Should().HaveCount(2);
    }

    [Fact]
    public async Task RetireSuperseded_OneReplayRowForTwoLegacyRows_KeepsBothOnEveryRun()
    {
        var member = await SeedMember();

        var stored = await Retire(
            Legacy(member, subholding: ""),
            Legacy(member, subholding: "Joint Ownership LPL Account"),
            Keyed(member, 121)
        );

        stored.Should().HaveCount(3);
        var rerun = await BuildSut().RetireSuperseded(CancellationToken.None);
        rerun.Should().Be(0);
    }

    [Fact]
    public async Task RetireSuperseded_EveryLegacyRowPairsWithItsOwnReplayRow_RetiresThemAll()
    {
        var member = await SeedMember();

        var stored = await Retire(
            Legacy(member, subholding: ""),
            Legacy(member, subholding: "Joint Ownership LPL Account"),
            Keyed(member, 121),
            Keyed(member, 122, subholding: "Individual Account")
        );

        stored.Should().HaveCount(2).And.OnlyContain(trade => trade.SourceId != null);
    }

    [Fact]
    public async Task RetireSuperseded_LegacyRowCollapsedTwoFiledRows_KeepsBothReplayRows()
    {
        // The old semantic unique index stored two identical filed rows (AAON listed twice in one
        // House report) as one legacy row.
        var member = await SeedMember();

        var stored = await Retire(Legacy(member), Keyed(member, 0), Keyed(member, 1));

        stored.Should().HaveCount(2).And.OnlyContain(trade => trade.SourceId != null);
        var rerun = await BuildSut().RetireSuperseded(CancellationToken.None);
        rerun.Should().Be(0);
    }
}
