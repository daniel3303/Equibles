using System.Text.RegularExpressions;

namespace Equibles.UnitTests.Deployment;

public class DockerComposeWorkerTopologyTests
{
    [Fact]
    public void ComposeDefinitions_OptionalCapabilities_ReuseSingleWorkerService()
    {
        var repositoryRoot = FindRepositoryRoot();
        var composeFiles = Directory.GetFiles(repositoryRoot, "docker-compose*.yml");
        var workerServices = composeFiles
            .SelectMany(ReadWorkerServiceNames)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        workerServices
            .Should()
            .Equal(
                ["worker"],
                "optional Compose files must extend the one scraper owner instead of racing it"
            );

        composeFiles
            .Sum(file =>
                Regex
                    .Matches(
                        File.ReadAllText(file),
                        @"dockerfile:\s+src/Equibles\.Worker\.Host/Dockerfile"
                    )
                    .Count
            )
            .Should()
            .Be(1, "the Compose stack must build exactly one full scraper host");
    }

    private static IEnumerable<string> ReadWorkerServiceNames(string composeFile)
    {
        var contents = File.ReadAllText(composeFile);
        return Regex
            .Matches(contents, @"(?m)^  (?<name>worker(?:-[a-z0-9-]+)?):\s*$")
            .Select(match => match.Groups["name"].Value);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
