using System.Reflection;
using Equibles.Plugins;

namespace Equibles.UnitTests.Plugins;

public class PluginLoaderHasPluginAttributeCorruptFileTests
{
    private static readonly MethodInfo HasPluginAttributeMethod = typeof(PluginLoader).GetMethod(
        "HasPluginAttribute",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // Contract (PluginLoader XML-doc): third-party DLLs are opt-in and LoadAll
    // runs at startup over every *.dll in the app directory. A file that exists
    // but is NOT a valid PE/.NET image (PEReader throws BadImageFormatException)
    // must be safely treated as "not a plugin" — false, no throw — or one stray
    // junk .dll crashes startup. Existing pins cover missing-file and
    // valid-DLL-without-attr; the corrupt-but-present file is a distinct catch.
    [Fact]
    public void HasPluginAttribute_FileExistsButIsNotAValidAssembly_ReturnsFalseWithoutThrowing()
    {
        var junkDll = Path.Combine(Path.GetTempPath(), $"equibles-junk-{Guid.NewGuid():N}.dll");
        File.WriteAllText(junkDll, "this is plain text, not a PE image at all");
        try
        {
            var act = () => (bool)HasPluginAttributeMethod.Invoke(null, [junkDll]);

            act.Should().NotThrow().Which.Should().BeFalse();
        }
        finally
        {
            File.Delete(junkDll);
        }
    }
}
