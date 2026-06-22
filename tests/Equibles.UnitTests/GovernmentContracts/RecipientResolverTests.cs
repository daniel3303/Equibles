using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.GovernmentContracts.HostedService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

public class RecipientResolverTests
{
    [Fact]
    public void Resolve_MatchesByNormalizedName_NotRawString()
    {
        var stockId = Guid.NewGuid();
        // The lookup is keyed by the normalized company name, exactly as BuildLookup produces it.
        var lookup = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            [RecipientNameNormalizer.Normalize("Lockheed Martin Corporation")] = stockId,
        };

        // Contract: resolution is an exact match on the NORMALIZED key, so a differently
        // suffixed/punctuated recipient name still resolves — raw string equality would not.
        RecipientResolver.Resolve("LOCKHEED MARTIN CORP.", lookup).Should().Be(stockId);
    }

    [Fact]
    public async Task BuildLookup_DropsKeySharedByMoreThanOneDistinctStock()
    {
        var options = NewDbOptions();
        using (var seed = NewContext(options))
        {
            // "Acme Corporation" and "Acme Corp" both normalize to "ACME" but are distinct stocks —
            // ambiguous. "Zenith Industries" is unique.
            seed.AddRange(
                new CommonStock
                {
                    Ticker = "ACMEA",
                    Name = "Acme Corporation",
                    Cik = "1",
                },
                new CommonStock
                {
                    Ticker = "ACMEB",
                    Name = "Acme Corp",
                    Cik = "2",
                },
                new CommonStock
                {
                    Ticker = "ZNTH",
                    Name = "Zenith Industries",
                    Cik = "3",
                }
            );
            await seed.SaveChangesAsync();
        }

        var lookup = await new RecipientResolver(ScopeFactory(options)).BuildLookup(
            CancellationToken.None
        );

        // Contract: a key shared by more than one distinct stock is dropped so a wrong link is
        // never asserted, while an unambiguous name still resolves.
        RecipientResolver.Resolve("Acme Corp", lookup).Should().BeNull();
        RecipientResolver.Resolve("Zenith Industries", lookup).Should().NotBeNull();
    }

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

    private static IServiceScopeFactory ScopeFactory(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext(options));
        services.AddScoped<CommonStockRepository>();
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
