namespace Equibles.Data;

/// <summary>
/// The set of module configurations bound to a single context type. Registered
/// as a singleton per context (<c>ModuleConfigurationSet&lt;EquiblesFinancialDbContext&gt;</c>,
/// <c>ModuleConfigurationSet&lt;EquiblesCustomerDbContext&gt;</c>, …) so each context
/// resolves only its own modules instead of a shared
/// <c>IEnumerable&lt;IModuleConfiguration&gt;</c>.
/// </summary>
public sealed class ModuleConfigurationSet<TContext>
    where TContext : EquiblesDbContextBase
{
    public IReadOnlyList<IModuleConfiguration> Modules { get; }

    public ModuleConfigurationSet(IEnumerable<IModuleConfiguration> modules)
    {
        Modules = modules.ToList();
    }

    /// <summary>
    /// Convenience conversion so callers that already hold a module array (tests,
    /// design-time factories) can pass it straight to a context constructor.
    /// </summary>
    public static implicit operator ModuleConfigurationSet<TContext>(
        IModuleConfiguration[] modules
    ) => new(modules);
}
