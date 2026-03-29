using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Tests.Helpers;

public static class TestDbContextFactory {
    public static EquiblesDbContext Create(params IModuleConfiguration[] modules) {
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;

        var context = new EquiblesDbContext(options, modules);
        context.Database.EnsureCreated();
        return context;
    }
}
