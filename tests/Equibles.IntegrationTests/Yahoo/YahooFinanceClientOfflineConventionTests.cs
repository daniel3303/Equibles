namespace Equibles.IntegrationTests.Yahoo;

/// <summary>
/// No integration test may reach Yahoo: a test that builds the plain client must seed the session
/// statics (<c>WithSeededSession</c>), and any other uses <see cref="Helpers.OfflineYahooFinanceClient"/>.
/// </summary>
public class YahooFinanceClientOfflineConventionTests
{
    [Fact]
    public void IntegrationTests_NeverStartALiveYahooSession()
    {
        var root = Path.Combine(RepositoryRoot(), "tests", "Equibles.IntegrationTests");
        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("new YahooFinanceClient(")
                    && !source.Contains("WithSeededSession(");
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "the plain client's session handshake calls Yahoo and fails when Yahoo throttles CI"
            );
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
