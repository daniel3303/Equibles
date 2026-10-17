using System.IO.Compression;
using System.Net;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class Form13FNoticeSyncServiceTests : ParadeDbMcpTestBase
{
    private const string Older = "01mar2026-31may2026_form13f.zip";
    private const string Newer = "01jun2026-31aug2026_form13f.zip";
    private const string Unpublished = "01sep2026-30nov2026_form13f.zip";

    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();

    public Form13FNoticeSyncServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SyncMissing_ReadsProcessedDataSetsNewestFirstAndOnlyOnce()
    {
        await MarkProcessed(Older, Newer);
        Serve(Older, Archive());
        Serve(Newer, Archive());
        string[] names = [Older, Newer, Unpublished];

        (await Sut().SyncMissing(names, 1, CancellationToken.None)).Should().Be(1);
        (await LedgerNames()).Should().Equal(Newer);
        (await Sut().SyncMissing(names, 1, CancellationToken.None)).Should().Be(1);
        (await Sut().SyncMissing(names, 1, CancellationToken.None)).Should().Be(0);

        (await LedgerNames()).Should().BeEquivalentTo([Older, Newer]);
        await _edgar
            .DidNotReceive()
            .DownloadStream(Arg.Is<string>(url => url.EndsWith(Unpublished)));
        await using var verify = Fixture.CreateDbContext();
        // Each accession is stored once, under the data set that read it last.
        (await verify.Set<Form13FNotice>().CountAsync())
            .Should()
            .Be(4);
        (await verify.Set<Form13FNoticeReporter>().CountAsync()).Should().Be(8);
        (await verify.Set<Form13FNoticeDataSet>().SingleAsync(d => d.FileName == Newer))
            .NoticeCount.Should()
            .Be(4);
        // Notices never create holders or filings.
        (await verify.Set<InstitutionalHolder>().CountAsync())
            .Should()
            .Be(0);
        (await verify.Set<InstitutionalFiling>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_SameAccessionAgain_ReplacesItsReporters()
    {
        using (var first = Archive())
            await Sut().Import(first, Older, CancellationToken.None);
        // The restated Simmons notice names only Simmons Bank.
        using (
            var second = Archive(
                (table, text) =>
                    table == "OTHERMANAGER.tsv"
                        ? string.Join(
                            '\n',
                            text.Split('\n').Where(line => !line.Contains("King Luther"))
                        )
                        : text
            )
        )
            await Sut().Import(second, Newer, CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var simmons = await verify
            .Set<Form13FNotice>()
            .Include(notice => notice.Reporters)
            .SingleAsync(notice => notice.AccessionNumber == "0001193125-26-328872");
        simmons.DataSetFileName.Should().Be(Newer);
        simmons.Reporters.Select(r => r.Name).Should().Equal("Simmons Bank");
        (await verify.Set<Form13FNotice>().CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task SyncMissing_MissingArchive_IsSkippedAndTheNextIsRead()
    {
        await MarkProcessed(Older, Newer);
        Serve(Older, Archive());
        _edgar
            .DownloadStream(Arg.Is<string>(url => url.EndsWith(Newer)))
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));
        _edgar
            .DownloadStream(HoldingsDataSetClient.CatalogUrl)
            .ThrowsAsync(new HttpRequestException("Not found", null, HttpStatusCode.NotFound));

        (await Sut().SyncMissing([Older, Newer], 5, CancellationToken.None)).Should().Be(1);

        (await LedgerNames()).Should().Equal(Older);
    }

    // Real rows from SEC's 01jun2026-31aug2026 data set: four notices among two 13F-HR reports.
    private static ZipArchive Archive(Func<string, string, string> edit = null)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var table in new[] { "SUBMISSION.tsv", "COVERPAGE.tsv", "OTHERMANAGER.tsv" })
            {
                var text = File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "TestAssets",
                        "Holdings",
                        "13f-notices",
                        table
                    )
                );
                using var writer = new StreamWriter(archive.CreateEntry(table).Open());
                writer.Write(edit == null ? text : edit(table, text));
            }
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private void Serve(string fileName, ZipArchive archive)
    {
        var buffer = new MemoryStream();
        using (var copy = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                using var source = entry.Open();
                using var target = copy.CreateEntry(entry.FullName).Open();
                source.CopyTo(target);
            }
        }

        var bytes = buffer.ToArray();
        _edgar
            .DownloadStream(Arg.Is<string>(url => url.EndsWith("/" + fileName)))
            .Returns(_ => new MemoryStream(bytes));
    }

    private async Task MarkProcessed(params string[] fileNames)
    {
        foreach (var fileName in fileNames)
            DbContext.Add(
                new ProcessedDataSet
                {
                    FileName = fileName,
                    ParserVersion = ProcessedDataSet.CurrentParserVersion,
                }
            );
        await DbContext.SaveChangesAsync();
    }

    private async Task<List<string>> LedgerNames()
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify.Set<Form13FNoticeDataSet>().Select(d => d.FileName).ToListAsync();
    }

    private Form13FNoticeSyncService Sut() =>
        new(
            Fixture.CreateDbContext(),
            new HoldingsDataSetClient(_edgar, NullLogger<HoldingsDataSetClient>()),
            new TsvParser(),
            NullLogger<Form13FNoticeSyncService>()
        );
}
