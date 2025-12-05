using Microsoft.EntityFrameworkCore;

namespace Equibles.Data;

public class EquiblesDbContext : DbContext {
    private readonly IEnumerable<IModuleConfiguration> _modules;

    public EquiblesDbContext(DbContextOptions<EquiblesDbContext> options, IEnumerable<IModuleConfiguration> modules)
        : base(options) {
        _modules = modules;
    }

    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        foreach (var module in _modules) {
            module.ConfigureEntities(builder);
        }
    }
}
