using System.Collections.Generic;
using System.Linq;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Media.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Helpers;

public static class TestDbContextFactory
{
    public static EquiblesFinancialDbContext Create(params IModuleConfiguration[] modules)
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

        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;

        var context = new EquiblesFinancialDbContext(options, ensured.ToArray());
        context.Database.EnsureCreated();
        return context;
    }
}
