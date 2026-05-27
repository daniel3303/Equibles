using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Helpers;

public static class TestDbContextFactory
{
    public static EquiblesFinancialDbContext Create(params IModuleConfiguration[] modules)
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;

        var context = new EquiblesFinancialDbContext(options, modules);
        context.Database.EnsureCreated();
        return context;
    }
}
