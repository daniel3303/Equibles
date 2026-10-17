using System.Text.RegularExpressions;

namespace Equibles.UnitTests.Holdings.Intelligence;

// 13D/G imports write holdings and filing rollups too, sometimes dated on a quarter end, so every
// holder-intelligence load of either table must name Form 13F or a single stake joins a 13F book.
public class HolderIntelligenceLoadConventionTests
{
    [Fact]
    public void IntelligenceHoldingLoads_Are13FFiltered()
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "Equibles.Holdings.HostedService",
                "Services",
                "HolderIntelligenceRefreshService.cs"
            )
        );
        var loads = Regex.Matches(source, @"Set<Institutional(Holding|Filing)>\(\)");

        loads.Should().NotBeEmpty();
        foreach (Match load in loads)
        {
            var statement = source[load.Index..source.IndexOf(';', load.Index)];
            statement
                .Should()
                .Contain("FilingType.Form13F", "every holding or filing load is Form 13F only");
        }
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
