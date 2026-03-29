namespace Equibles.Data;

public class EquiblesModuleBuilder {
    internal List<IModuleConfiguration> Modules { get; } = [];

    public EquiblesModuleBuilder AddModule<T>() where T : IModuleConfiguration, new() {
        if (Modules.Any(m => m.GetType() == typeof(T))) {
            return this;
        }

        Modules.Add(new T());
        return this;
    }

    public EquiblesModuleBuilder AddAllModules() {
        var moduleTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => {
                try { return a.DefinedTypes; } catch { return []; }
            })
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IModuleConfiguration).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .ToList();

        foreach (var type in moduleTypes) {
            if (Modules.Any(m => m.GetType() == type)) continue;
            Modules.Add((IModuleConfiguration)Activator.CreateInstance(type));
        }

        return this;
    }
}
