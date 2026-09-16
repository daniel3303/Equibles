using System.IO.Compression;
using Equibles.EquityMarkets.BusinessLogic.Firds;
using Equibles.EquityMarkets.Data.Catalog;
using Equibles.EquityMarkets.Data.Models;
using Equibles.EquityMarkets.Repositories;
using Equibles.Integrations.Esma;
using Equibles.Integrations.Esma.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.EquityMarkets;

/// <summary>
/// Contract: the newest complete full set replaces the authority's universe (rows it no longer states
/// are retired, not deleted), later deltas are applied once each in publication order, an already
/// recorded file is never downloaded again, and the primary-venue share gate answers from the result.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FirdsUniverseImporterTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly FirstFull = new(2026, 9, 12);
    private static readonly DateOnly SecondFull = new(2026, 9, 19);

    private static string Fixture(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "EquityMarkets", "Firds", name)
        );

    private FirdsUniverseImporter Importer(FakeIndex index) =>
        new(
            [index],
            ServiceScopeSubstitute.Create(
                (typeof(FirdsImportRunRepository), new FirdsImportRunRepository(DbContext)),
                (
                    typeof(FirdsInstrumentRecordRepository),
                    new FirdsInstrumentRecordRepository(DbContext)
                )
            ),
            NullLogger<FirdsUniverseImporter>()
        );

    private Task<List<FirdsInstrumentRecord>> Rows() =>
        DbContext
            .Set<FirdsInstrumentRecord>()
            .AsNoTracking()
            .OrderBy(row => row.Isin)
            .ToListAsync();

    [Fact]
    public async Task FullSetThenDeltas_StoreEveryEquityLineOnceAndAnswerThePrimaryVenueGate()
    {
        var full = Fixture("FULINS_E_sample.xml");
        var index = new FakeIndex();
        index.AddFull(FirstFull, full, EmptyFull(full));
        index.AddDelta(new DateOnly(2026, 9, 15), 1, 1, Fixture("DLTINS_sample.xml"));

        await Importer(index).Import(CancellationToken.None);

        var rows = await Rows();
        rows.Should()
            .HaveCount(
                10,
                "seven equity lines from the full set plus three delta records; the structured product and the bond are not equities"
            );
        rows.Should()
            .AllSatisfy(row =>
            {
                row.Authority.Should().Be(EsmaFirdsClient.AuthorityCode);
                row.RemovedAt.Should().BeNull();
                row.Cfi.Should().StartWith("E").And.NotMatch("EY*");
            });
        rows.Single(row => row.Isin == "DE0005558696").TerminationDate.Should().NotBeNull();
        var runs = await DbContext
            .Set<FirdsImportRun>()
            .AsNoTracking()
            .OrderBy(run => run.FileName)
            .ToListAsync();
        runs.Select(run => (run.FileName, run.Kind, run.RowsRead, run.RowsStored))
            .Should()
            .Equal(
                ("DLTINS_20260915_01of01.zip", FirdsFileKind.Delta, 4, 3),
                ("FULINS_E_20260912_01of02.zip", FirdsFileKind.Full, 8, 7),
                ("FULINS_E_20260912_02of02.zip", FirdsFileKind.Full, 0, 0)
            );
        runs.Should().AllSatisfy(run => run.Checksum.Should().Be(index.Checksum(run.FileName)));
        var records = new FirdsInstrumentRecordRepository(DbContext);
        var now = DateTime.UtcNow;
        var lisbon = EquityMarketCatalog.TryGet("euronext-lisbon");
        var paris = EquityMarketCatalog.TryGet("euronext-paris");
        (
            await records.GetLiveShare(
                "PTSLB0AM0010",
                lisbon.FirdsAuthority,
                lisbon.FirdsVenueCodes,
                now
            )
        )
            .Should()
            .NotBeNull();
        (
            await records.GetLiveShare(
                "FR0005691656",
                paris.FirdsAuthority,
                paris.FirdsVenueCodes,
                now
            )
        )
            .Should()
            .NotBeNull();
        (
            await records.GetLiveShare(
                "FR0005691656",
                lisbon.FirdsAuthority,
                lisbon.FirdsVenueCodes,
                now
            )
        )
            .Should()
            .BeNull("the line is filed on Paris, not on a Lisbon venue");
        (await records.GetLiveShare("FI0009800395", EsmaFirdsClient.AuthorityCode, ["AQEA"], now))
            .RelevantTradingVenue.Should()
            .Be("DHEL", "the line is live on Aquis while its most relevant venue is Helsinki");
        (await records.GetLiveShare("SE0030026894", EsmaFirdsClient.AuthorityCode, ["SSME"], now))
            .Should()
            .BeNull("the line terminated on 2026-09-11");
        (await records.GetLiveShare("CA00791L1067", EsmaFirdsClient.AuthorityCode, ["DUSB"], now))
            .Should()
            .BeNull("a depositary receipt is not a share");
        (await records.GetLiveShare("US7591EP8869", EsmaFirdsClient.AuthorityCode, ["FRAB"], now))
            .Should()
            .NotBeNull("a preference share is a traded share line");
        (await records.CountHomeShares(lisbon, now)).Should().Be(1);
        (await records.CountHomeShares(paris, now)).Should().Be(1);
        (
            await records.GetLiveShare(
                "FR0005691656",
                FcaFirdsClient.AuthorityCode,
                paris.FirdsVenueCodes,
                now
            )
        )
            .Should()
            .BeNull("the FCA register stores nothing for a Paris line");
        (await records.CountHomeShares(EquityMarketCatalog.TryGet("nasdaq-helsinki"), now))
            .Should()
            .Be(
                1,
                "Aquis-quoted Raisio has its home on Nasdaq Helsinki's Nordic@Mid segment DHEL, a catalogued home venue"
            );
        (await records.CountHomeShares(EquityMarketCatalog.TryGet("nasdaq-stockholm"), now))
            .Should()
            .Be(0, "no Stockholm line is in the fixture");
        (
            await new FirdsImportRunRepository(DbContext).GetLatestFullPublication(
                EsmaFirdsClient.AuthorityCode
            )
        )
            .Should()
            .Be(FirstFull);
    }

    [Fact]
    public async Task RecordedFiles_AreNeverDownloadedAgain()
    {
        var full = Fixture("FULINS_E_sample.xml");
        var index = new FakeIndex();
        index.AddFull(FirstFull, full, EmptyFull(full));
        index.AddDelta(new DateOnly(2026, 9, 15), 1, 1, Fixture("DLTINS_sample.xml"));
        await Importer(index).Import(CancellationToken.None);
        index.Downloads.Should().HaveCount(3);

        await Importer(index).Import(CancellationToken.None);

        index.Downloads.Should().HaveCount(3);
        index
            .ListedSince.Should()
            .Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14), FirstFull);
        (await Rows()).Should().HaveCount(10);
        (await DbContext.Set<FirdsImportRun>().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task ANewerFullSet_RetiresEveryLineItNoLongerStatesAndRestatesTheOnesItKeeps()
    {
        var full = Fixture("FULINS_E_sample.xml");
        var index = new FakeIndex();
        index.AddFull(FirstFull, full, EmptyFull(full));
        index.AddDelta(new DateOnly(2026, 9, 15), 1, 1, Fixture("DLTINS_sample.xml"));
        await Importer(index).Import(CancellationToken.None);
        var before = (await Rows()).Single(row => row.Isin == "PTSLB0AM0010").ObservedAt;
        index.AddFull(SecondFull, OnlyFirstRecord(full));

        await Importer(index).Import(CancellationToken.None);

        var rows = await Rows();
        rows.Should().HaveCount(10, "retired lines are kept as the delisted-names record");
        var kept = rows.Single(row => row.Isin == "PTSLB0AM0010");
        kept.RemovedAt.Should().BeNull();
        kept.ObservedAt.Should().BeAfter(before);
        rows.Where(row => row.Isin != "PTSLB0AM0010")
            .Should()
            .AllSatisfy(row => row.RemovedAt.Should().NotBeNull());
        var records = new FirdsInstrumentRecordRepository(DbContext);
        var now = DateTime.UtcNow;
        (await records.GetLive(now).CountAsync()).Should().Be(1);
        (await records.GetLiveShare("FR0005691656", EsmaFirdsClient.AuthorityCode, ["XPAR"], now))
            .Should()
            .BeNull();
        (
            await new FirdsImportRunRepository(DbContext).GetLatestFullPublication(
                EsmaFirdsClient.AuthorityCode
            )
        )
            .Should()
            .Be(SecondFull);
        (await DbContext.Set<FirdsImportRun>().CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task AnIncompleteFullSet_IsIgnoredUntilEveryPartIsPublished()
    {
        var full = Fixture("FULINS_E_sample.xml");
        var index = new FakeIndex();
        index.AddFull(FirstFull, full, EmptyFull(full));
        await Importer(index).Import(CancellationToken.None);
        index.AddPart(SecondFull, 1, 2, OnlyFirstRecord(full));

        await Importer(index).Import(CancellationToken.None);

        (
            await new FirdsImportRunRepository(DbContext).GetLatestFullPublication(
                EsmaFirdsClient.AuthorityCode
            )
        )
            .Should()
            .Be(FirstFull);
        (await Rows()).Should().HaveCount(7).And.AllSatisfy(row => row.RemovedAt.Should().BeNull());
        index.Downloads.Should().HaveCount(2);
    }

    [Fact]
    public async Task AFullPartLargerThanOneBatch_IsStoredWholeWithinTheParameterCeiling()
    {
        const int records = 5_000;
        var full = Fixture("FULINS_E_sample.xml");
        var index = new FakeIndex();
        index.AddFull(FirstFull, Synthetic(full, records));

        await Importer(index).Import(CancellationToken.None);

        (await Rows())
            .Should()
            .HaveCount(records)
            .And.AllSatisfy(row =>
            {
                row.Mic.Should().Be("XLIS");
                row.RemovedAt.Should().BeNull();
            });
        var run = await DbContext.Set<FirdsImportRun>().AsNoTracking().SingleAsync();
        (run.RowsRead, run.RowsStored).Should().Be((records, records));
    }

    [Fact]
    public async Task ATerminationReportedWithoutADate_LeavesTheLiveUniverseAtOnce()
    {
        var full = Fixture("FULINS_E_sample.xml");
        var index = new FakeIndex();
        index.AddFull(FirstFull, full, EmptyFull(full));
        var undated = Fixture("DLTINS_sample.xml")
            .Replace("<TermntnDt>2020-12-31T22:59:59Z</TermntnDt>", "");
        undated.Should().NotBe(Fixture("DLTINS_sample.xml"));
        index.AddDelta(new DateOnly(2026, 9, 15), 1, 1, undated);

        await Importer(index).Import(CancellationToken.None);

        var paragon = (await Rows()).Single(row => row.Isin == "DE0005558696");
        paragon.TerminationDate.Should().BeNull();
        paragon
            .RemovedAt.Should()
            .NotBeNull("a terminated record without a date is no longer live");
        var records = new FirdsInstrumentRecordRepository(DbContext);
        (await records.GetLive(DateTime.UtcNow).Select(row => row.Isin).ToListAsync())
            .Should()
            .NotContain("DE0005558696")
            .And.Contain("PTSLB0AM0010");
    }

    // The first record of the full file repeated under fresh, check-digit-valid ISINs.
    private static string Synthetic(string full, int count)
    {
        var start = full.IndexOf("<RefData", StringComparison.Ordinal);
        var firstEnd =
            full.IndexOf("</RefData>", start, StringComparison.Ordinal) + "</RefData>".Length;
        var end = full.LastIndexOf("</RefData>", StringComparison.Ordinal) + "</RefData>".Length;
        var template = full[start..firstEnd];
        template.Should().Contain("PTSLB0AM0010");
        var records = new System.Text.StringBuilder();
        for (var index = 0; index < count; index++)
            records.Append(template.Replace("PTSLB0AM0010", Isin("PT", index)));
        return full[..start] + records + full[end..];
    }

    private static string Isin(string country, int number)
    {
        var body = country + number.ToString("D9");
        var digits = string.Concat(
            body.Select(character =>
                char.IsLetter(character) ? (character - 'A' + 10).ToString() : character.ToString()
            )
        );
        var sum = 0;
        var doubleIt = true;
        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';
            if (doubleIt)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }
            sum += digit;
            doubleIt = !doubleIt;
        }
        return body + (10 - sum % 10) % 10;
    }

    // The same header with no records: part two of a set whose equities all sit in part one.
    private static string EmptyFull(string full)
    {
        var start = full.IndexOf("<RefData", StringComparison.Ordinal);
        var end = full.LastIndexOf("</RefData>", StringComparison.Ordinal) + "</RefData>".Length;
        return full[..start] + full[end..];
    }

    private static string OnlyFirstRecord(string full)
    {
        var start = full.IndexOf("<RefData", StringComparison.Ordinal);
        var firstEnd =
            full.IndexOf("</RefData>", start, StringComparison.Ordinal) + "</RefData>".Length;
        var end = full.LastIndexOf("</RefData>", StringComparison.Ordinal) + "</RefData>".Length;
        return full[..firstEnd] + full[end..];
    }

    private sealed class FakeIndex : IFirdsFileIndex
    {
        private readonly List<(FirdsFile File, byte[] Zip)> _files = [];

        public List<string> Downloads { get; } = [];
        public List<DateOnly> ListedSince { get; } = [];

        public string Authority => EsmaFirdsClient.AuthorityCode;

        public void AddFull(DateOnly publishedOn, params string[] parts)
        {
            for (var index = 0; index < parts.Length; index++)
                AddPart(publishedOn, index + 1, parts.Length, parts[index]);
        }

        public void AddPart(DateOnly publishedOn, int part, int of, string xml) =>
            Add(
                $"FULINS_E_{publishedOn:yyyyMMdd}_{part:00}of{of:00}.zip",
                FirdsFileType.Full,
                publishedOn,
                xml
            );

        public void AddDelta(DateOnly publishedOn, int part, int of, string xml) =>
            Add(
                $"DLTINS_{publishedOn:yyyyMMdd}_{part:00}of{of:00}.zip",
                FirdsFileType.Delta,
                publishedOn,
                xml
            );

        public string Checksum(string fileName) =>
            _files.Single(entry => entry.File.FileName == fileName).File.Checksum;

        private void Add(string name, FirdsFileType type, DateOnly publishedOn, string xml)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            using (
                var writer = new StreamWriter(
                    archive.CreateEntry(Path.ChangeExtension(name, ".xml")).Open()
                )
            )
                writer.Write(xml);
            var zip = buffer.ToArray();
            _files.Add(
                (
                    new FirdsFile
                    {
                        Authority = Authority,
                        FileName = name,
                        FileType = type,
                        PublishedOn = publishedOn,
                        DownloadUrl = new Uri("https://firds.esma.europa.eu/firds/" + name),
                        Checksum = Convert.ToHexStringLower(
                            System.Security.Cryptography.MD5.HashData(zip)
                        ),
                    },
                    zip
                )
            );
        }

        public Task<IReadOnlyList<FirdsFile>> ListEquityFiles(
            DateOnly publishedOnOrAfter,
            CancellationToken cancellationToken = default
        )
        {
            ListedSince.Add(publishedOnOrAfter);
            IReadOnlyList<FirdsFile> listed = _files
                .Select(entry => entry.File)
                .Where(file => file.PublishedOn >= publishedOnOrAfter)
                .OrderBy(file => file.PublishedOn)
                .ThenBy(file => file.FileName)
                .ToList();
            return Task.FromResult(listed);
        }

        public async Task<FirdsDownload> Download(
            FirdsFile file,
            CancellationToken cancellationToken = default
        )
        {
            Downloads.Add(file.FileName);
            var zip = _files.Single(entry => entry.File.FileName == file.FileName).Zip;
            var path = Path.Combine(
                Path.GetTempPath(),
                "firds-test-" + Guid.NewGuid().ToString("N") + ".zip"
            );
            await File.WriteAllBytesAsync(path, zip, cancellationToken);
            return new FirdsDownload(file, path, zip.Length);
        }
    }
}
