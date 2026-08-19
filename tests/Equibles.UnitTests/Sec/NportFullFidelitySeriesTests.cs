using Equibles.Sec.HostedService.Helpers;
using Microsoft.Extensions.Configuration;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Reading the opt-in list. Getting this wrong fails silently in the worst way: an unparsed value
/// yields an empty set, the sweep keeps narrowing, and the only symptom is a fund's schedule being
/// quietly short — so both configuration shapes a deployment can express are pinned here.
/// </summary>
public class NportFullFidelitySeriesTests
{
    [Fact]
    public void FromConfiguration_Missing_IsEmpty()
    {
        Read().Should().BeEmpty();
    }

    [Fact]
    public void FromConfiguration_ArrayForm_ReadsEveryElement()
    {
        var seriesIds = Read(
            ("NportSweep:FullFidelitySeriesIds:0", "S000030000"),
            ("NportSweep:FullFidelitySeriesIds:1", "S000030003")
        );

        seriesIds.Should().BeEquivalentTo(["S000030000", "S000030003"]);
    }

    [Fact]
    public void FromConfiguration_DelimitedScalar_ReadsEveryEntry()
    {
        // The shape a container deployment can express in one environment variable.
        var seriesIds = Read(
            ("NportSweep:FullFidelitySeriesIds", "S000030000, S000030003 ;S000030017")
        );

        seriesIds.Should().BeEquivalentTo(["S000030000", "S000030003", "S000030017"]);
    }

    [Fact]
    public void FromConfiguration_BlankEntries_AreDropped()
    {
        var seriesIds = Read(("NportSweep:FullFidelitySeriesIds", " , S000030000 ,, "));

        seriesIds.Should().ContainSingle().Which.Should().Be("S000030000");
    }

    [Fact]
    public void FromConfiguration_MatchesCaseInsensitively()
    {
        // EDGAR writes series ids uppercase; a hand-typed configuration value must still match.
        var seriesIds = Read(("NportSweep:FullFidelitySeriesIds", "s000030000"));

        seriesIds.Contains("S000030000").Should().BeTrue();
    }

    private static IReadOnlySet<string> Read(params (string key, string value)[] settings) =>
        NportFullFidelitySeries.FromConfiguration(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    settings.Select(s => new KeyValuePair<string, string>(s.key, s.value))
                )
                .Build()
        );
}
