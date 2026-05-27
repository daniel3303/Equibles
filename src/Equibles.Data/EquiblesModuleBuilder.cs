namespace Equibles.Data;

public class EquiblesModuleBuilder
{
    internal List<IModuleConfiguration> Modules { get; } = [];

    public EquiblesModuleBuilder AddModule<T>()
        where T : IModuleConfiguration, new()
    {
        if (Modules.Any(m => m.GetType() == typeof(T)))
        {
            return this;
        }

        Modules.Add(new T());
        return this;
    }

    public EquiblesModuleBuilder AddAllModules()
    {
        return AddAllModulesOfType<IModuleConfiguration>();
    }

    /// <summary>
    /// Adds every loaded module implementing <typeparamref name="TMarker"/>
    /// (e.g. <see cref="IFinancialModule"/> or <see cref="ICustomerModule"/>), so
    /// each context registers only the modules for its own domain.
    /// Like <see cref="AddAllModules"/>, this only sees assemblies already loaded
    /// into the AppDomain — it does not force-load module DLLs. Hosts that must
    /// guarantee a module is present should register it explicitly via its
    /// <c>Add{Module}()</c> extension.
    /// </summary>
    public EquiblesModuleBuilder AddAllModulesOfType<TMarker>()
        where TMarker : IModuleConfiguration
    {
        var moduleTypes = AppDomain
            .CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try
                {
                    return a.DefinedTypes;
                }
                catch
                {
                    return [];
                }
            })
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(TMarker).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) != null
            )
            .ToList();

        foreach (var type in moduleTypes)
        {
            if (Modules.Any(m => m.GetType() == type))
                continue;
            Modules.Add((IModuleConfiguration)Activator.CreateInstance(type));
        }

        return this;
    }
}
