using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(HistoricalEquityDbCollection.Name)]
public class EquityIdentityFoundationTests : ParadeDbMcpTestBase
{
    public EquityIdentityFoundationTests(HistoricalEquityDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public void Migration_AddsNativeIdentityTables_AndRefusesDestructiveRollback()
    {
        var type = typeof(Equibles.Migrations.DesignTimeDbContextFactory)
            .Assembly.GetTypes()
            .Single(type => type.Name == "AddEquityIdentityFoundation");
        var migration = (Migration)Activator.CreateInstance(type);
        migration
            .UpOperations.OfType<CreateTableOperation>()
            .Select(table => table.Name)
            .Should()
            .BeEquivalentTo("EquityIssuer", "EquitySecurity", "EquityListing");
        Action rollback = () =>
        {
            _ = migration.DownOperations;
        };
        rollback.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public async Task CrossListing_SharesIssuerAndSecurity_WithoutChangingLegacyLookup()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SAME",
            Name: "Existing issuer"
        );
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        var issuer = await DbContext.Set<EquityIssuer>().SingleAsync();
        var ordinary = Security(issuer);
        var receipt = Security(issuer);
        receipt.SecurityType = EquitySecurityKind.DepositaryReceipt;
        var lisbon = Listing(ordinary, "XLIS");
        var london = Listing(ordinary, "XLON");
        var us = Listing(receipt, "XNYS");
        DbContext.AddRange(lisbon, london, us);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await new EquityIssuerRepository(DbContext).GetUsByTicker("SAME"))
            .Id.Should()
            .Be(stock.Id);
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(4);
        (await DbContext.Set<EquitySecurity>().CountAsync()).Should().Be(3);
        (await DbContext.Set<EquityIssuer>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ActiveTickerCollision_IsRejectedWithinAnExchange()
    {
        DbContext.AddRange(Listing(Security(Issuer())), Listing(Security(Issuer())));
        Func<Task> save = () => DbContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task InactiveListing_DoesNotPreventTickerReuse_AndRetainsItsIdentity()
    {
        var old = Listing(Security(Issuer()));
        old.Active = false;
        old.DelistedOn = new DateOnly(2025, 1, 1);
        var current = Listing(Security(Issuer()));
        DbContext.AddRange(old, current);
        await DbContext.SaveChangesAsync();
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(2);
        old.Id.Should().NotBe(current.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidQuoteScale_IsRejected(decimal scale)
    {
        var listing = Listing(Security(Issuer()));
        listing.QuoteUnitMultiplier = scale;
        DbContext.Add(listing);
        Func<Task> save = () => DbContext.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task LegacyDeletion_PreservesNewIdentityAndListings()
    {
        var stock = new CommonStock { Ticker = "SAME", Name = "Existing issuer" };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await DbContext.Set<CommonStock>().Where(row => row.Id == stock.Id).ExecuteDeleteAsync();
        DbContext
            .Entry(await DbContext.Set<EquityIssuer>().SingleAsync())
            .Property<Guid?>("CommonStockId")
            .CurrentValue.Should()
            .BeNull();
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task IssuerDeletion_CannotCascadeThroughRetainedSecurities()
    {
        DbContext.Add(Listing(Security(Issuer())));
        await DbContext.SaveChangesAsync();
        Func<Task> delete = () => DbContext.Set<EquityIssuer>().ExecuteDeleteAsync();
        await delete.Should().ThrowAsync<Npgsql.PostgresException>();
        (await DbContext.Set<EquityListing>().CountAsync()).Should().Be(1);
    }

    private static EquityIssuer Issuer() =>
        new() { Name = "Source issuer", IdentitySourceUrl = "https://example.org/issuer" };

    private static EquitySecurity Security(EquityIssuer issuer) =>
        new()
        {
            Issuer = issuer,
            SecurityType = EquitySecurityKind.OrdinaryShare,
            IdentitySourceUrl = "https://example.org/security",
        };

    private static EquityListing Listing(EquitySecurity security, string mic = "XLIS") =>
        new()
        {
            Security = security,
            IdentityState = EquityIdentityState.Verified,
            MarketIdentifierCode = mic,
            Ticker = "SAME",
            TradingCurrency = "EUR",
            QuoteUnitMultiplier = 1m,
            IdentitySourceUrl = "https://example.org/listing",
        };
}
