using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace Equibles.Plugins;

/// <summary>
/// Discovers and loads plugin assemblies from the application directory.
/// <list type="bullet">
///   <item>Equibles.*.dll files are loaded unconditionally (convention-based).</item>
///   <item>Other DLLs are loaded only if marked with <see cref="EquiblesPluginAttribute"/> (third-party opt-in).</item>
/// </list>
/// Call <see cref="LoadAll"/> at startup before any reflection-based discovery (AddAllModules, AddAllRepositories).
/// </summary>
public static class PluginLoader {
    private static Assembly[] _loaded;

    public static Assembly[] LoadAll() {
        if (_loaded != null) return _loaded;

        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .ToDictionary(a => a.GetName().Name);
        var plugins = new List<Assembly>(alreadyLoaded.Values);

        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "*.dll")) {
            try {
                var assemblyName = AssemblyName.GetAssemblyName(dll);
                if (alreadyLoaded.ContainsKey(assemblyName.Name)) continue;

                var fileName = Path.GetFileName(dll);
                var isEquibles = fileName.StartsWith("Equibles.", StringComparison.OrdinalIgnoreCase);

                if (!isEquibles && !HasPluginAttribute(dll)) continue;

                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                plugins.Add(assembly);
                alreadyLoaded[assemblyName.Name] = assembly;
            } catch (BadImageFormatException) { }
        }

        _loaded = plugins.ToArray();
        return _loaded;
    }

    private static bool HasPluginAttribute(string dllPath) {
        try {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata) return false;

            var reader = peReader.GetMetadataReader();
            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes()) {
                var attr = reader.GetCustomAttribute(handle);
                if (attr.Constructor.Kind != HandleKind.MemberReference) continue;

                var ctor = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                if (ctor.Parent.Kind != HandleKind.TypeReference) continue;

                var typeRef = reader.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                if (reader.GetString(typeRef.Namespace) == "Equibles.Plugins"
                    && reader.GetString(typeRef.Name) == nameof(EquiblesPluginAttribute))
                    return true;
            }
        } catch {
            return false;
        }

        return false;
    }
}
