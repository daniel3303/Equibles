namespace Equibles.UnitTests.Holdings;

// A parent link says "reports for", while the like-for-like comparison groups only CIKs that
// continue one another's 13F reports, so comparison grouping must never read the link tables.
public class HolderLinkReadConventionTests
{
    private static readonly string[] LinkTypes =
    [
        "HolderLinkEvidence",
        "HolderRollup",
        "FundSeriesHolderLink",
        "HolderLinkRule",
        "HolderLinkRefreshService",
    ];

    [Theory]
    [InlineData("Equibles.Holdings.BusinessLogic", "HoldingsComparisonCoverage.cs")]
    [InlineData("Equibles.Holdings.BusinessLogic", "HoldingsSuccessionGroups.cs")]
    [InlineData("Equibles.Holdings.HostedService", "Services", "HolderSuccessionRule.cs")]
    [InlineData("Equibles.Holdings.HostedService", "Services", "HolderSuccessionRefreshService.cs")]
    public void ComparisonGrouping_NeverReadsLinks(params string[] path)
    {
        var file = Path.Combine([RepositoryRoot(), "src", .. path]);
        File.Exists(file).Should().BeTrue();
        var source = File.ReadAllText(file);

        foreach (var type in LinkTypes)
            source
                .Should()
                .NotContain(type, "succession grouping stays independent of parent links");
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
