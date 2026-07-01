using System.Linq;
using Equibles.Data;
using Equibles.Media.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Helpers;

public static class TestDbContextFactory
{
    public static EquiblesFinancialDbContext Create(params IModuleConfiguration[] modules)
    {
        // File — with its value-converted StorageProvider property — is referenced by many
        // modules via navigation, so the Media module must always configure it. Without it,
        // EF discovers File through another module's nav but never applies the converter, then
        // treats StorageProvider as an entity and model-building throws. Mirror production and
        // the ParadeDb fixture, which always include Media.
        IModuleConfiguration[] withMedia = modules.Any(m => m is MediaModuleConfiguration)
            ? modules
            : [.. modules, new MediaModuleConfiguration()];

        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;

        var context = new EquiblesFinancialDbContext(options, withMedia);
        context.Database.EnsureCreated();
        return context;
    }
}
