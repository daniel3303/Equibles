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
