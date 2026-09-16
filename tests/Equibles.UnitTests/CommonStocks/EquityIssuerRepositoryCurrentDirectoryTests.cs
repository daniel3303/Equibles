using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: <c>GetCurrentDirectory</c> is the live directory across markets, an issuer whose
/// active presentation listing is either US or a verified venue listing. <c>GetCurrentUsDirectory</c>
/// keeps its US-only universe, a legacy non-US presentation belongs to neither, and the
/// in-memory twin <c>IsCurrentDirectoryIssuer</c> agrees with the query on every seeded shape.
/// </summary>
public class EquityIssuerRepositoryCurrentDirectoryTests
{
    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static EquityIssuer Us(string ticker, bool active = true) =>
        EquityIssuerSeed.Create(Ticker: ticker, Active: active, Name: ticker + " Inc");

    private static EquityIssuer Venue(
        string ticker,
        EquityIdentityState state,
        bool active = true,
        string country = "FR",
        string mic = "XPAR"
    ) =>
        EquityIssuerSeed.Create(
            Ticker: ticker,
            Active: active,
            Name: ticker + " SA",
            MarketCountryCode: country,
            MarketIdentifierCode: mic,
            IdentityState: state,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );

    private sealed record Seeded(
        EquityIssuer UsActive,
        EquityIssuer UsDelisted,
        EquityIssuer VerifiedParis,
        EquityIssuer VerifiedParisInactive,
        EquityIssuer LegacyParis,
        EquityIssuer NoPresentation
    );

    private static async Task<Seeded> Seed(DbContextOptions<EquiblesFinancialDbContext> options)
    {
        var seeded = new Seeded(
            Us("AIR"),
            Us("DEAD", active: false),
            Venue("AIR", EquityIdentityState.Verified),
            Venue("GONE", EquityIdentityState.Verified, active: false),
            Venue("OLD", EquityIdentityState.Legacy),
            new EquityIssuer { Name = "Shell company" }
        );
        using var ctx = NewContext(options);
        ctx.Set<EquityIssuer>()
            .AddRange(
                seeded.UsActive,
                seeded.UsDelisted,
                seeded.VerifiedParis,
                seeded.VerifiedParisInactive,
                seeded.LegacyParis,
                seeded.NoPresentation
            );
        await ctx.SaveChangesAsync();
        return seeded;
    }

    [Fact]
    public async Task CurrentDirectory_IsActiveUsPlusVerifiedVenuePresentations()
    {
        var options = NewDbOptions();
        var seeded = await Seed(options);
        using var ctx = NewContext(options);
        var repository = new EquityIssuerRepository(ctx);

        var current = await repository.GetCurrentDirectory().Select(i => i.Id).ToListAsync();
        var us = await repository.GetCurrentUsDirectory().Select(i => i.Id).ToListAsync();

        current.Should().BeEquivalentTo([seeded.UsActive.Id, seeded.VerifiedParis.Id]);
        us.Should().BeEquivalentTo([seeded.UsActive.Id]);
    }

    [Fact]
    public async Task CurrentDirectoryByIds_FiltersToTheRequestedIssuers()
    {
        var options = NewDbOptions();
        var seeded = await Seed(options);
        using var ctx = NewContext(options);
        var repository = new EquityIssuerRepository(ctx);

        var ids = await repository
            .GetCurrentDirectoryByIds([seeded.VerifiedParis.Id, seeded.LegacyParis.Id])
            .Select(i => i.Id)
            .ToListAsync();

        ids.Should().BeEquivalentTo([seeded.VerifiedParis.Id]);
    }

    [Fact]
    public async Task CurrentDirectoryIssuer_LoadsOnlyDirectoryMembers()
    {
        var options = NewDbOptions();
        var seeded = await Seed(options);
        using var ctx = NewContext(options);
        var repository = new EquityIssuerRepository(ctx);

        (await repository.GetCurrentDirectoryIssuer(seeded.UsActive.Id)).Should().NotBeNull();
        (await repository.GetCurrentDirectoryIssuer(seeded.VerifiedParis.Id)).Should().NotBeNull();
        (await repository.GetCurrentDirectoryIssuer(seeded.UsDelisted.Id)).Should().BeNull();
        (await repository.GetCurrentDirectoryIssuer(seeded.VerifiedParisInactive.Id))
            .Should()
            .BeNull();
        (await repository.GetCurrentDirectoryIssuer(seeded.LegacyParis.Id)).Should().BeNull();
        (await repository.GetCurrentDirectoryIssuer(seeded.NoPresentation.Id)).Should().BeNull();
        (await repository.GetCurrentUsDirectoryIssuer(seeded.VerifiedParis.Id))
            .Should()
            .BeNull("the US directory stays US-only");
    }

    [Fact]
    public async Task InMemoryTwin_AgreesWithTheQuery()
    {
        var options = NewDbOptions();
        var seeded = await Seed(options);
        using var ctx = NewContext(options);
        var repository = new EquityIssuerRepository(ctx);
        var current = (
            await repository.GetCurrentDirectory().Select(i => i.Id).ToListAsync()
        ).ToHashSet();

        foreach (
            var issuer in new[]
            {
                seeded.UsActive,
                seeded.UsDelisted,
                seeded.VerifiedParis,
                seeded.VerifiedParisInactive,
                seeded.LegacyParis,
                seeded.NoPresentation,
            }
        )
            EquityIssuerRepository
                .IsCurrentDirectoryIssuer(issuer)
                .Should()
                .Be(current.Contains(issuer.Id), issuer.Name);
        EquityIssuerRepository.IsCurrentDirectoryIssuer(null).Should().BeFalse();
    }
}
