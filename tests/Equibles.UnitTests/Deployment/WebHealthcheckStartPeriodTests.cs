using System.Text.RegularExpressions;

namespace Equibles.UnitTests.Deployment;

public class WebHealthcheckStartPeriodTests
{
    // The web container runs pending EF migrations before Kestrel binds and
    // /healthz answers. On a populated production database a single heavy
    // backfill migration (e.g. a full-table UPDATE on the multi-million-row
    // InsiderTransaction table) takes minutes. Docker only starts counting
    // failed health probes against the container AFTER --start-period elapses,
    // so the start period is the real budget the migration phase gets before
    // the container is marked unhealthy and dependents (worker, mcp) abort
    // with "dependency failed to start ... is unhealthy".
    //
    // A 15s start period (the original value) gives a ~65s budget
    // (start-period + retries x interval) that a real upgrade outlives, making
    // a successful upgrade look like a broken release. This test pins a floor
    // well above observed migration times so the budget can't silently shrink
    // back below it.
    private static readonly TimeSpan MinimumStartPeriod = TimeSpan.FromMinutes(5);

    [Fact]
    public void WebDockerfile_HealthcheckStartPeriod_CoversLongStartupMigrations()
    {
        var dockerfile = File.ReadAllText(FindWebDockerfile());

        var startPeriod = ParseHealthcheckStartPeriod(dockerfile);

        startPeriod
            .Should()
            .BeGreaterThanOrEqualTo(
                MinimumStartPeriod,
                "the web healthcheck start period is the budget startup migrations run in "
                    + "before the container is marked unhealthy and worker/mcp abort"
            );
    }

    private static TimeSpan ParseHealthcheckStartPeriod(string dockerfile)
    {
        var match = Regex.Match(
            dockerfile,
            @"HEALTHCHECK\b[^\n]*?--start-period=(?<value>\S+)",
            RegexOptions.IgnoreCase
        );

        match
            .Success.Should()
            .BeTrue("the web Dockerfile must declare a HEALTHCHECK with --start-period");

        return ParseDockerDuration(match.Groups["value"].Value);
    }

    // Docker durations are a concatenation of decimal-number + unit pairs
    // (e.g. "10m", "1h30m", "90s"). Supports the units a healthcheck realistically uses.
    private static TimeSpan ParseDockerDuration(string value)
    {
        var matches = Regex.Matches(value, @"(?<amount>\d+)(?<unit>ms|h|m|s)");

        matches.Count.Should().BeGreaterThan(0, $"'{value}' is not a recognizable Docker duration");

        var total = TimeSpan.Zero;
        foreach (Match part in matches)
        {
            var amount = int.Parse(part.Groups["amount"].Value);
            total += part.Groups["unit"].Value switch
            {
                "h" => TimeSpan.FromHours(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "s" => TimeSpan.FromSeconds(amount),
                "ms" => TimeSpan.FromMilliseconds(amount),
                _ => TimeSpan.Zero,
            };
        }

        return total;
    }

    private static string FindWebDockerfile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Equibles.Web", "Dockerfile");
            if (
                File.Exists(candidate)
                && File.Exists(Path.Combine(directory.FullName, "docker-compose.yml"))
            )
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate src/Equibles.Web/Dockerfile by walking up from the test output directory."
        );
    }
}
