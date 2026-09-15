namespace Equibles.UnitTests.CorporateActions;

// Every restatement reader dereferences StockSplit.Listing, often after the loading scope is
// gone, so a raw Set<StockSplit>() load without the listing is a lazy load waiting to throw.
// StockSplitQueries.ForIssuers and StockSplitRepository.GetAll are the two loads that include it.
public class StockSplitLoadConventionTests
{
    [Fact]
    public void OnlyTheSharedQueryHelperLoadsSplitsFromTheRawSet()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "src");
        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            )
            .Where(path => File.ReadAllText(path).Contains("Set<StockSplit>()"))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .ToList();

        offenders
            .Should()
            .Equal(
                [Path.Combine("Equibles.CorporateActions.Data", "StockSplitQueries.cs")],
                "load splits through StockSplitQueries.ForIssuers or StockSplitRepository so the listing travels with the row"
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
