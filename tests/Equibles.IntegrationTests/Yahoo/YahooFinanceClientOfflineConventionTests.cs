using System.Reflection;
using System.Text.RegularExpressions;
using Equibles.Integrations.Yahoo;

namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// No test may reach Yahoo: the plain client's session handshake calls Yahoo with its own HttpClient,
/// so tests build <see cref="Helpers.OfflineYahooFinanceClient"/> or another subclass that cans it.
/// </summary>
public class YahooFinanceClientOfflineConventionTests
{
    // Direct construction of the plain client: explicit, fully qualified, or target-typed in a
    // declaration, an expression-bodied member or a bare return.
    private static readonly Regex PlainConstruction = new(
        @"new\s+(?:[\w.]+\.)?YahooFinanceClient\s*\("
            + @"|YahooFinanceClient\s+\w+\s*=\s*new\s*\("
            + @"|YahooFinanceClient\s+\w+\s*\([^)]*\)\s*(?:=>|\{\s*return)\s*new\s*\(",
        RegexOptions.CultureInvariant
    );

    [Fact]
    public void Tests_NeverConstructThePlainClient()
    {
        var root = RepositoryRoot();
        var offenders = new[] { "Equibles.IntegrationTests", "Equibles.UnitTests" }
            .SelectMany(project =>
                Directory.EnumerateFiles(
                    Path.Combine(root, "tests", project),
                    "*.cs",
                    SearchOption.AllDirectories
                )
            )
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(path => PlainConstruction.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "the plain client's session handshake calls Yahoo and fails when Yahoo throttles CI"
            );
    }

    [Fact]
    public void EveryTestSubclass_CansTheSessionHandshake()
    {
        var subclasses = typeof(YahooFinanceClientOfflineConventionTests)
            .Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(YahooFinanceClient)))
            .ToList();

        subclasses.Should().NotBeEmpty();
        subclasses
            .Where(type =>
                type.GetMethod(
                    "EnsureSession",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                ) == null
            )
            .Should()
            .BeEmpty("a subclass that keeps the base EnsureSession still calls Yahoo");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Equibles.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
