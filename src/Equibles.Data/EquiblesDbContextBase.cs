using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Equibles.Data;

/// <summary>
/// Shared base for every Equibles DB context. Holds the module-configuration
/// loop only — no Postgres extensions, so a context can target plain managed
/// Postgres (e.g. the customer context) while another opts into pgvector /
/// ParadeDB in its own <see cref="OnModelCreating"/> override.
/// Each concrete context receives its own module list via
/// <see cref="ModuleConfigurationSet{TContext}"/>, so two contexts in the same
/// host never share a configuration set.
/// </summary>
public abstract class EquiblesDbContextBase : DbContext
{
    private readonly IReadOnlyList<IModuleConfiguration> _modules;

    protected EquiblesDbContextBase(
        DbContextOptions options,
        IReadOnlyList<IModuleConfiguration> modules
    )
        : base(options)
    {
        _modules = modules;
    }

    /// <summary>
    /// Stable identity of this context's module composition, used by
    /// <see cref="ModuleAwareModelCacheKeyFactory"/> so contexts of the same type
    /// built from different module sets cache distinct models. Constant per
    /// context type in production.
    /// </summary>
    internal string ModuleCacheKey =>
        string.Join(
            ",",
            _modules.Select(m => m.GetType().FullName).OrderBy(name => name, StringComparer.Ordinal)
        );

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ModuleAwareModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        foreach (var module in _modules)
        {
            module.ConfigureEntities(builder);
        }
    }
}
