using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Migrations.Migrations;
using Equibles.Yahoo.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class NativeEquityModelTests(HistoricalEquityDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task ProductionModel_HasNoRetiredStorage_AndMatchesItsMigrationSnapshot()
    {
        await using var native = fixture.CreateNativeDbContext();
        var retired = new[]
        {
            "CommonStock",
            "LegacyEquityListing",
            "DailyStockPrice",
            "ListedDailyStockPrice",
        };
        native
            .Model.GetEntityTypes()
            .Select(type => type.GetTableName())
            .Should()
            .NotIntersectWith(retired);
        native
            .Model.FindEntityType(typeof(EquityIssuer))
            .FindProperty("CommonStockId")
            .Should()
            .BeNull();
        native
            .Model.FindEntityType(typeof(EquityIssuer))
            .FindNavigation("CommonStock")
            .Should()
            .BeNull();
        native.Database.HasPendingModelChanges().Should().BeFalse();
        new DetachRetiredEquityStorageModel()
            .UpOperations.Should()
            .BeEmpty("physical retirement follows the native application rollout");
        foreach (var type in new[] { typeof(CommonStock), typeof(LegacyEquityListing) })
            typeof(EquityIssuer).Assembly.GetType(type.FullName).Should().BeNull();
        foreach (var type in new[] { typeof(DailyStockPrice), typeof(LegacyDailyStockPrice) })
            typeof(EquityDailyStockPrice).Assembly.GetType(type.FullName).Should().BeNull();
    }

    [Fact]
    public async Task NativeContext_CanReadAndUpdateMigratedIssuer_WithoutMappingItsOriginalRow()
    {
        var source = new CommonStock
        {
            Name = "Original issuer",
            Ticker = "NATIVE-MODEL",
            Description = "Preserved source",
        };
        DbContext.Add(source);
        await DbContext.SaveChangesAsync();
        await using var native = fixture.CreateNativeDbContext();
        var issuer = await native.Set<EquityIssuer>().SingleAsync(row => row.Id == source.Id);
        issuer.Description.Should().Be(source.Description);
        issuer.Description = "Native issuer update";
        await native.SaveChangesAsync();
        await DbContext.Entry(source).ReloadAsync();
        source.Description.Should().Be("Preserved source");
        native.Set<EquityIssuer>().ToQueryString().Should().NotContain("CommonStock");
        (await DbContext.Set<CommonStock>().CountAsync()).Should().Be(1);
    }
}
