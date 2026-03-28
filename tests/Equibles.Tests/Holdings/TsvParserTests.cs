using System.IO.Compression;
using System.Text;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.Tests.Holdings;

public class TsvParserTests {
    private readonly TsvParser _sut = new();

    [Fact]
    public async Task ParseEntry_ValidTsv_ReturnsRows() {
        var tsv = "NAME\tAGE\tCITY\nAlice\t30\tNew York\nBob\t25\tBoston";
        using var archive = CreateZipArchive(tsv);

        var rows = await CollectRows(archive.Entries[0]);

        rows.Should().HaveCount(2);
        rows[0]["NAME"].Should().Be("Alice");
        rows[0]["AGE"].Should().Be("30");
        rows[0]["CITY"].Should().Be("New York");
        rows[1]["NAME"].Should().Be("Bob");
    }

    [Fact]
    public async Task ParseEntry_HeadersAreCaseInsensitive() {
        var tsv = "Name\tAge\nAlice\t30";
        using var archive = CreateZipArchive(tsv);

        var rows = await CollectRows(archive.Entries[0]);

        rows[0]["name"].Should().Be("Alice");
        rows[0]["NAME"].Should().Be("Alice");
        rows[0]["Name"].Should().Be("Alice");
    }

    [Fact]
    public async Task ParseEntry_EmptyLinesAreSkipped() {
        var tsv = "COL\nA\n\n  \nB";
        using var archive = CreateZipArchive(tsv);

        var rows = await CollectRows(archive.Entries[0]);

        rows.Should().HaveCount(2);
        rows[0]["COL"].Should().Be("A");
        rows[1]["COL"].Should().Be("B");
    }

    [Fact]
    public async Task ParseEntry_HeaderOnly_ReturnsNoRows() {
        var tsv = "COL1\tCOL2";
        using var archive = CreateZipArchive(tsv);

        var rows = await CollectRows(archive.Entries[0]);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseEntry_EmptyFile_ReturnsNoRows() {
        using var archive = CreateZipArchive("");

        var rows = await CollectRows(archive.Entries[0]);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseEntry_FewerValuesThanHeaders_MapsAvailableColumns() {
        var tsv = "A\tB\tC\n1\t2";
        using var archive = CreateZipArchive(tsv);

        var rows = await CollectRows(archive.Entries[0]);

        rows.Should().ContainSingle();
        rows[0].Should().ContainKey("A");
        rows[0].Should().ContainKey("B");
        rows[0].Should().NotContainKey("C");
    }

    [Fact]
    public async Task ParseEntry_TrimsWhitespace() {
        var tsv = " NAME \t AGE \n Alice \t 30 ";
        using var archive = CreateZipArchive(tsv);

        var rows = await CollectRows(archive.Entries[0]);

        rows[0]["NAME"].Should().Be("Alice");
        rows[0]["AGE"].Should().Be("30");
    }

    private static ZipArchive CreateZipArchive(string content) {
        var stream = new MemoryStream();
        using (var writeArchive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            var entry = writeArchive.CreateEntry("data.tsv");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private async Task<List<Dictionary<string, string>>> CollectRows(ZipArchiveEntry entry) {
        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in _sut.ParseEntry(entry)) {
            rows.Add(row);
        }
        return rows;
    }
}
