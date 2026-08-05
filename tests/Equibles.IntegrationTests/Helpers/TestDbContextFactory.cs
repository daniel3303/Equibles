using System.Collections.Generic;
using System.Linq;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Media.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Helpers;

public static class TestDbContextFactory
{
    public static EquiblesFinancialDbContext Create(params IModuleConfiguration[] modules)
    {
        return Create(
            ignoreInMemoryTransactions: false,
            populateMissingPriceTickers: true,
            modules
        );
    }

    public static EquiblesFinancialDbContext CreateIgnoringInMemoryTransactions(
        params IModuleConfiguration[] modules
    )
    {
        return Create(ignoreInMemoryTransactions: true, populateMissingPriceTickers: true, modules);
    }

    public static EquiblesFinancialDbContext CreateIgnoringInMemoryTransactionsWithoutPriceSeedDefaults(
        params IModuleConfiguration[] modules
    )
    {
        return Create(
            ignoreInMemoryTransactions: true,
            populateMissingPriceTickers: false,
            modules
        );
    }

    private static EquiblesFinancialDbContext Create(
        bool ignoreInMemoryTransactions,
        bool populateMissingPriceTickers,
        params IModuleConfiguration[] modules
    )
    {
        // Two modules must always be present because their entities are referenced across
        // module boundaries and get pulled into the model transitively:
        //  - Media: File carries a value-converted StorageProvider; without the converter EF
        //    treats it as an entity and model-building throws.
        //  - CorporateActions: StockSplit is queried by split-adjustment used from Finra/Yahoo/
        //    Holdings/Insider managers; without it the StockSplit DbSet can't be resolved.
        // Mirror production and the ParadeDb fixture, which always include both.
        var ensured = new List<IModuleConfiguration>(modules);
        if (!ensured.Any(m => m is MediaModuleConfiguration))
        {
            ensured.Add(new MediaModuleConfiguration());
        }
        if (!ensured.Any(m => m is CorporateActionsModuleConfiguration))
        {
            ensured.Add(new CorporateActionsModuleConfiguration());
        }

        var optionsBuilder = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false);
        if (populateMissingPriceTickers)
            optionsBuilder.AddInterceptors(new DailyStockPriceSeedInterceptor());
        if (ignoreInMemoryTransactions)
            optionsBuilder.ConfigureWarnings(w =>
                w.Ignore(InMemoryEventId.TransactionIgnoredWarning)
            );

        var context = new EquiblesFinancialDbContext(optionsBuilder.Options, ensured.ToArray());
        context.Database.EnsureCreated();
        return context;
    }
}
