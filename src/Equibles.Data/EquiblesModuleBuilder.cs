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
}
