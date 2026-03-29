using System.Reflection;
using System.Runtime.Loader;

namespace Equibles.Data;

/// <summary>
/// Ensures all Equibles.*.dll assemblies are loaded into the current AppDomain.
/// Required because .NET lazily loads assemblies — in published (Docker) builds,
/// assemblies may not be loaded when reflection-based discovery runs.
/// </summary>
public static class EquiblesAssemblyLoader {
    private static int _loaded;

    public static void EnsureLoaded() {
        if (Interlocked.Exchange(ref _loaded, 1) == 1) return;

        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .ToHashSet();

        var baseDir = AppContext.BaseDirectory;
        foreach (var dll in Directory.GetFiles(baseDir, "Equibles.*.dll")) {
            try {
                var assemblyName = AssemblyName.GetAssemblyName(dll);
                if (loaded.Contains(assemblyName.Name)) continue;
                AssemblyLoadContext.Default.LoadFromAssemblyName(assemblyName);
                loaded.Add(assemblyName.Name);
            } catch (BadImageFormatException) {
                // Not a managed assembly (native interop DLL) — skip
            }
        }
    }
}
