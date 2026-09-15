using Equibles.EquityMarkets.BusinessLogic.Firds;
using Equibles.Integrations.Esma;
using Equibles.Integrations.Esma.Models;

namespace Equibles.UnitTests.EquityMarkets;

public class FirdsRecordReaderTests
{
    private static FileStream Fixture(string name) =>
        File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "EquityMarkets", "Firds", name)
        );

    private static async Task<List<FirdsRecord>> Read(string name)
    {
        await using var xml = Fixture(name);
        var records = new List<FirdsRecord>();
        await foreach (var record in FirdsRecordReader.Read(xml))
            records.Add(record);
        return records;
    }

    [Fact]
    public async Task FullReport_YieldsEveryRecordWithItsStatedIdentityVenueAndLifecycle()
    {
        var records = await Read("FULINS_E_sample.xml");
        records.Should().HaveCount(8);
        records.Should().AllSatisfy(record => record.Kind.Should().Be(FirdsRecordKind.Full));
        var lisbon = records.Single(record => record.Isin == "PTSLB0AM0010");
        lisbon.Cfi.Should().Be("ESRTFR");
        lisbon.Mic.Should().Be("XLIS");
        lisbon.RelevantTradingVenue.Should().Be("XLIS");
        lisbon.RelevantCompetentAuthority.Should().Be("PT");
        lisbon.Lei.Should().MatchRegex("^[A-Z0-9]{20}$");
        lisbon.Currency.Should().Be("EUR");
        lisbon.FullName.Should().NotBeNullOrWhiteSpace();
        lisbon.TerminationDate.Should().BeNull();
        lisbon.FirstTradeDate.Should().NotBeNull();
        lisbon.FirstTradeDate.Value.Kind.Should().Be(DateTimeKind.Utc);
        var secondary = records.Single(record => record.Isin == "FI0009800395");
        secondary.Mic.Should().Be("AQEA");
        secondary.RelevantTradingVenue.Should().Be("DHEL");
        var terminated = records.Single(record => record.Isin == "SE0030026894");
        terminated
            .TerminationDate.Should()
            .Be(new DateTime(2026, 9, 11, 23, 59, 59, DateTimeKind.Utc));
        records
            .Select(record => record.Cfi[..2])
            .Should()
            .BeEquivalentTo(["ES", "ES", "ES", "ES", "EP", "ED", "EY", "EL"]);
    }

    [Fact]
    public async Task DeltaReport_LabelsNewModifiedAndTerminatedRecords()
    {
        var records = await Read("DLTINS_sample.xml");
        records.Should().HaveCount(4);
        records
            .Single(record => record.Kind == FirdsRecordKind.New)
            .Isin.Should()
            .Be("US30609A1097");
        records
            .Single(record => record.Kind == FirdsRecordKind.Modified)
            .Isin.Should()
            .Be("DE0005218309");
        records
            .Where(record => record.Kind == FirdsRecordKind.Terminated)
            .Select(record => record.Isin)
            .Should()
            .BeEquivalentTo(["DE0005558696", "DE000BD22B23"]);
        records
            .Single(record => record.Isin == "DE0005558696")
            .TerminationDate.Should()
            .NotBeNull();
        records.Single(record => record.Cfi.StartsWith("RW")).Isin.Should().Be("DE000BD22B23");
    }

    [Fact]
    public async Task EquityClassFilter_KeepsSharesReceiptsAndUnitsButNotStructuredProducts()
    {
        var records = await Read("FULINS_E_sample.xml");
        records
            .Where(record => FirdsUniverseImporter.IsEquity(record.Cfi))
            .Select(record => record.Cfi[..2])
            .Should()
            .BeEquivalentTo(["ES", "ES", "ES", "ES", "EP", "ED", "EL"]);
        FirdsUniverseImporter.IsEquity("EYCYFS").Should().BeFalse();
        FirdsUniverseImporter.IsEquity("DBFSEB").Should().BeFalse();
        FirdsUniverseImporter.IsEquity(null).Should().BeFalse();
        FirdsUniverseImporter.IsEquity("ES").Should().BeFalse();
    }

    [Theory]
    [InlineData("<Id>PTSLB0AM0011</Id>", "<Id>PTSLB0AM0010</Id>")]
    [InlineData("<Issr>BAD</Issr>", "<Issr>213800EDIKU4Z4I1R529</Issr>")]
    [InlineData("<TermntnDt>tomorrow</TermntnDt>", "<TermntnDt>2026-09-11T23:59:59Z</TermntnDt>")]
    public async Task InvalidIdentifiersOrDates_AreRefusedRatherThanStored(
        string broken,
        string valid
    )
    {
        var xml = await File.ReadAllTextAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "EquityMarkets",
                "Firds",
                "FULINS_E_sample.xml"
            )
        );
        xml.Should().Contain(valid);
        await using var stream = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes(xml.Replace(valid, broken))
        );
        var read = async () =>
        {
            await foreach (var _ in FirdsRecordReader.Read(stream)) { }
        };
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData("FULINS_E_20260912_01of02.zip", FirdsFileType.Full, "2026-09-12")]
    [InlineData("DLTINS_20260915_04of04.zip", FirdsFileType.Delta, "2026-09-15")]
    public void FileNames_ParseTypeAndPublicationDate(string name, FirdsFileType type, string date)
    {
        FirdsFileNames.TryParse(name, out var parsed, out var publishedOn).Should().BeTrue();
        parsed.Should().Be(type);
        publishedOn.Should().Be(DateOnly.Parse(date));
    }

    [Theory]
    [InlineData("FULINS_C_20260912_01of01.zip")]
    [InlineData("FULINS_E_2026091_01of02.zip")]
    [InlineData("DLTINS_20260915.zip")]
    [InlineData(null)]
    public void OtherFileNames_AreNotEquityFiles(string name)
    {
        FirdsFileNames.TryParse(name, out _, out _).Should().BeFalse();
    }
}
