using System.IO.Compression;
using System.Text;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class SecRemoteFileTests
{
    private const string Url = "https://www.sec.gov/files/archive.zip";

    [Fact]
    public async Task ZipOverRanges_ReadsOneEntryWithoutDownloadingTheRest()
    {
        var random = new Random(7);
        var filler = new byte[200_000];
        random.NextBytes(filler);
        var bytes = Zip(
            ("filler.bin", filler),
            ("table.csv", Encoding.Latin1.GetBytes("FilingID\n42\n"))
        );
        var (client, fetched) = Client(bytes);

        await using var file = await SecRemoteFile.Open(client, Url, blockSize: 16_384);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("table.csv")!.Open(), Encoding.Latin1);

        (await reader.ReadToEndAsync()).Should().Be("FilingID\n42\n");
        file.Length.Should().Be(bytes.Length);
        fetched
            .Sum()
            .Should()
            .BeLessThan(bytes.Length / 4, "only the directory and the entry are read");
    }

    [Fact]
    public async Task Read_AcrossABlockBoundary_ReturnsTheExactBytes()
    {
        var bytes = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var (client, _) = Client(bytes);
        await using var file = await SecRemoteFile.Open(client, Url, blockSize: 16);

        file.Seek(10, SeekOrigin.Begin);
        var buffer = new byte[30];
        var total = 0;
        while (total < buffer.Length)
            total += file.Read(buffer, total, buffer.Length - total);

        buffer.Should().Equal(bytes.Skip(10).Take(30));
        file.Seek(-1, SeekOrigin.End).Should().Be(99);
        file.ReadByte().Should().Be(99);
        file.ReadByte().Should().Be(-1);
    }

    private static (ISecEdgarClient Client, List<int> Fetched) Client(byte[] bytes)
    {
        var fetched = new List<int>();
        var client = Substitute.For<ISecEdgarClient>();
        client
            .DownloadRange(Url, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var offset = call.ArgAt<long>(1);
                var count = (int)Math.Min(call.ArgAt<int>(2), bytes.Length - offset);
                fetched.Add(count);
                return Task.FromResult(
                    new SecByteRange(bytes.AsSpan((int)offset, count).ToArray(), bytes.Length)
                );
            });
        return (client, fetched);
    }

    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                stream.Write(content);
            }
        }
        return buffer.ToArray();
    }
}
