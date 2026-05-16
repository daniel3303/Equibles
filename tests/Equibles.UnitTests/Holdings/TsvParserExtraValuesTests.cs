using System.IO.Compression;
using System.Text;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class TsvParserExtraValuesTests
{
    private readonly TsvParser _sut = new();

    // Adversarial: a row carrying MORE tab-separated values than there are
    // headers is malformed input. The contract is positional header→value
    // mapping, so every declared header must still receive its own correct
    // value, the surplus value must not leak in under any key, and parsing
    // must not throw. (Sibling "fewer values than headers" is already pinned.)
    [Fact]
    public async Task ParseEntry_MoreValuesThanHeaders_MapsHeadersAndDropsSurplus()
    {
        var tsv = "NAME\tAGE\nAlice\t30\tLONDON";
        using var archive = CreateZipArchive(tsv);

        var rows = await CollectRows(archive.Entries[0]);

        rows.Should().ContainSingle();
        rows[0].Should().HaveCount(2);
        rows[0]["NAME"].Should().Be("Alice");
        rows[0]["AGE"].Should().Be("30");
        rows[0].Values.Should().NotContain("LONDON");
    }

    private static ZipArchive CreateZipArchive(string content)
    {
        var stream = new MemoryStream();
        using (var writeArchive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = writeArchive.CreateEntry("data.tsv");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private async Task<List<Dictionary<string, string>>> CollectRows(ZipArchiveEntry entry)
    {
        var rows = new List<Dictionary<string, string>>();
        await foreach (var row in _sut.ParseEntry(entry))
        {
            rows.Add(row);
        }
        return rows;
    }
}
