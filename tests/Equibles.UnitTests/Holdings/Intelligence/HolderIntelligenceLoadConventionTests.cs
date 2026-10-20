using System.Text.RegularExpressions;

namespace Equibles.UnitTests.Holdings.Intelligence;

// 13D/G imports write holdings and filing rollups too, sometimes dated on a quarter end, so every
// holder-intelligence load of either table must name Form 13F or a single stake joins a 13F book.
public class HolderIntelligenceLoadConventionTests
{
    [Theory]
    [InlineData("", "HolderIntelligenceRefreshService.cs")]
    [InlineData("Benchmarks", "AggregateHolderBenchmarkProvider.cs")]
    public void IntelligenceHoldingLoads_Are13FFiltered(string folder, string file)
    {
        var source = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "Equibles.Holdings.HostedService",
                "Services",
                folder,
                file
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

    // The stock signals count Schedule 13D and 13G stakes beside the 13F books, so a load there
    // may read the 13D/G pair, but never both tables' mixed rows.
    [Fact]
    public void SignalHoldingLoads_NameTheirFilingType()
    {
        var folder = Path.Combine(
            RepositoryRoot(),
            "src",
            "Equibles.Holdings.HostedService",
            "Services",
            "Signals"
        );
        var loads = Directory
            .GetFiles(folder, "*.cs")
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return Regex
                    .Matches(source, @"Set<Institutional(Holding|Filing)>\(\)")
                    .Select(load =>
                        (
                            Path.GetFileName(path),
                            Statement: source[load.Index..source.IndexOf(';', load.Index)]
                        )
                    );
            })
            .ToList();

        loads.Should().NotBeEmpty();
        foreach (var (file, statement) in loads)
            (
                statement.Contains("FilingType.Form13F")
                || (
                    statement.Contains("FilingType.Schedule13D")
                    && statement.Contains("FilingType.Schedule13G")
                )
            )
                .Should()
                .BeTrue($"{file} must load Form 13F rows or the Schedule 13D/G pair only");
    }

    // The signal readers sit behind 13F surfaces, so they read holdings only through the
    // 13F-typed repository seams and never load the tables directly.
    [Fact]
    public void SignalReaders_ReadOnly13FSeams()
    {
        var folder = Path.Combine(
            RepositoryRoot(),
            "src",
            "Equibles.Holdings.BusinessLogic",
            "Signals"
        );
        var sources = Directory
            .GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Select(path => (File: Path.GetFileName(path), Text: File.ReadAllText(path)))
            .ToList();

        var calls = sources
            .SelectMany(source =>
                Regex
                    .Matches(source.Text, @"_holdingRepository\s*\.\s*(\w+)\s*\(")
                    .Select(match => (source.File, Method: match.Groups[1].Value))
            )
            .ToList();
        calls.Should().NotBeEmpty();
        foreach (var (file, method) in calls)
            method.Should().StartWith("Get13F", $"{file} must read 13F rows only");
        foreach (var (file, text) in sources)
            text.Should()
                .NotMatchRegex(
                    @"Set<Institutional(Holding|Filing)>",
                    $"{file} must read holdings through the 13F repository seams"
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
