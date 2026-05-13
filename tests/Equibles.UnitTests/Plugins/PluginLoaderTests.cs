using System.Reflection;
using Equibles.Plugins;

namespace Equibles.UnitTests.Plugins;

/// <summary>
/// Tests for <see cref="PluginLoader"/>. The public <c>LoadAll</c> entry point caches
/// statically and walks <c>AppContext.BaseDirectory</c>, so we exercise the pure-logic
/// private metadata probe via reflection.
/// </summary>
public class PluginLoaderTests {
    private static readonly MethodInfo HasPluginAttributeMethod = typeof(PluginLoader)
        .GetMethod("HasPluginAttribute", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void HasPluginAttribute_RealDllWithoutPluginAttribute_ReturnsFalse() {
        // The companion NonExistentFile test exercises the catch-all failure
        // path. This pins the *success* walk on a real PE file: open metadata,
        // enumerate assembly-level custom attributes, find none matching
        // Equibles.Plugins.EquiblesPluginAttribute, return false. The test
        // assembly itself has no [assembly: EquiblesPlugin] — Equibles.* DLLs
        // are loaded by convention and don't need the marker. Without this
        // pin, a refactor that mismatches the namespace/name check (e.g.
        // typo'd "Equibles.Plugin" or compared against the wrong class) would
        // make HasPluginAttribute return true on every DLL, defeating the
        // third-party opt-in contract.
        var realDll = typeof(PluginLoaderTests).Assembly.Location;

        var result = (bool)HasPluginAttributeMethod.Invoke(null, [realDll]);

        result.Should().BeFalse();
    }

    [Fact]
    public void HasPluginAttribute_NonExistentFile_ReturnsFalse() {
        // PluginLoader walks every DLL in AppContext.BaseDirectory. Native libraries,
        // permission-blocked files, and DLLs deleted between Directory.GetFiles and
        // the metadata read are all real failure modes — the defensive try/catch
        // around the PE-reader path is the only thing preventing one bad file from
        // crashing the whole plugin-discovery pass at startup. Pin the
        // exception-swallowing contract on a path that does not exist so a refactor
        // can't narrow the catch (or remove it) without a test failure.
        var nonExistent = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.dll");

        var result = (bool)HasPluginAttributeMethod.Invoke(null, [nonExistent]);

        result.Should().BeFalse();
    }
}
