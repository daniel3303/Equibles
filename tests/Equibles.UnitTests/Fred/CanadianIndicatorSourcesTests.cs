using System.Globalization;
using Equibles.Fred.HostedService.Canada;

namespace Equibles.UnitTests.Fred;

// The Bank of Canada's Valet and Statistics Canada's WDS answers as they were served on 2026-11-02.
public class CanadianIndicatorSourcesTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fred", "Fixtures", name));

    [Fact]
    public void ValetObservationsAreReadAsStated_WhateverTheMachineCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var points = CanadianIndicatorSources.ParseValet(
                Fixture("valet-v39079-2026-09.json"),
                "V39079"
            );

            points.Should().HaveCount(44);
            points[0].Should().Be(new IndicatorPoint(new DateOnly(2026, 9, 1), 2.25m));
            points[^1].Should().Be(new IndicatorPoint(new DateOnly(2026, 10, 30), 2.50m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AValetAnswerForAnotherSeriesIsRefused()
    {
        var read = () =>
            CanadianIndicatorSources.ParseValet(Fixture("valet-v39079-2026-09.json"), "AVG.INTWO");

        read.Should().Throw<InvalidDataException>();
    }

    // One answer can carry several vectors; each series reads only its own.
    [Fact]
    public void StatisticsCanadaPointsAreReadForTheVectorAskedFor()
    {
        var json = Fixture("statcan-cpi-2026.json");

        var cpi = CanadianIndicatorSources.ParseStatCan(json, "41690973");
        var trim = CanadianIndicatorSources.ParseStatCan(json, "108785715");

        cpi.Should().HaveCount(9);
        cpi[^1].Should().Be(new IndicatorPoint(new DateOnly(2026, 9, 1), 170.0m));
        trim[^1].Should().Be(new IndicatorPoint(new DateOnly(2026, 9, 1), 1.8m));
        var other = () => CanadianIndicatorSources.ParseStatCan(json, "2062815");
        other.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void AddressesNameTheSeriesAndTheWindow()
    {
        CanadianIndicatorSources
            .ValetAddress("BD.CDN.10YR.DQ.YLD", new DateOnly(2026, 1, 2))
            .ToString()
            .Should()
            .Be(
                "https://www.bankofcanada.ca/valet/observations/BD.CDN.10YR.DQ.YLD/json?start_date=2026-01-02"
            );
        CanadianIndicatorSources
            .StatCanAddress("41690973", new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 2))
            .AbsoluteUri.Should()
            .Contain(
                "vectorIds=%2241690973%22&startRefPeriod=2026-01-01&endReferencePeriod=2026-11-02"
            );
    }
}
