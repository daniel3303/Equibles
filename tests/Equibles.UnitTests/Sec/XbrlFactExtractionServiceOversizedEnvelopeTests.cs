using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// The extractor's parsers materialise the whole envelope in memory, so a
/// nine-figure envelope can OOM the shared worker process (a 190 MB 6-K did,
/// repeatedly, under memory pressure). Extract must refuse such envelopes
/// before loading their content — returning the same "nothing to persist"
/// result a clean zero-fact parse does, so the sweep stamps the document
/// terminal instead of retrying the OOM — while envelopes at or under the
/// ceiling (and legacy rows with no recorded size) still parse.
/// </summary>
public class XbrlFactExtractionServiceOversizedEnvelopeTests
{
    /// <summary>
    /// Minimal valid gzipped envelope: parses cleanly to zero facts, so Extract
    /// returns before it needs a service scope or the database.
    /// </summary>
    private sealed class RecordingFileManager : IFileManager
    {
        public bool ContentLoaded { get; private set; }

        public Task<byte[]> GetContent(File file)
        {
            ContentLoaded = true;
            return Task.FromResult(GzipCompressor.Compress("<html></html>"u8.ToArray()));
        }

        public Task<File> SaveFile(byte[] content, string fileName, bool protect = false) =>
            throw new NotSupportedException();

        public Task<File> SaveInternalFile(
            byte[] content,
            string name,
            string extension,
            string contentType,
            string tier = null
        ) => throw new NotSupportedException();

        public Task<Stream> OpenRead(File file) => throw new NotSupportedException();

        public void DeleteFile(File file) => throw new NotSupportedException();
    }

    private static (XbrlFactExtractionService Service, RecordingFileManager Files) BuildService()
    {
        var files = new RecordingFileManager();
        var service = new XbrlFactExtractionService(
            scopeFactory: null,
            new InlineXbrlParser(),
            new StandaloneXbrlParser(),
            files,
            NullLogger<XbrlFactExtractionService>.Instance
        );
        return (service, files);
    }

    private static Document CapturedDocument(long? uncompressedSize) =>
        new()
        {
            AccessionNumber = "0001493152-24-017248",
            XbrlStatus = XbrlCaptureStatus.Captured,
            XbrlContent = new File(),
            XbrlUncompressedSize = uncompressedSize,
        };

    [Fact]
    public async Task Extract_EnvelopeAboveParseCeiling_SkipsWithoutLoadingContent()
    {
        var (service, files) = BuildService();
        var document = CapturedDocument(XbrlFactExtractionService.MaxParseableEnvelopeBytes + 1);

        var persisted = await service.Extract(document, CancellationToken.None);

        persisted.Should().Be(0);
        files.ContentLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task Extract_EnvelopeAtParseCeiling_LoadsContent()
    {
        var (service, files) = BuildService();
        var document = CapturedDocument(XbrlFactExtractionService.MaxParseableEnvelopeBytes);

        await service.Extract(document, CancellationToken.None);

        files.ContentLoaded.Should().BeTrue();
    }

    [Fact]
    public async Task Extract_EnvelopeWithUnknownSize_LoadsContent()
    {
        var (service, files) = BuildService();
        var document = CapturedDocument(uncompressedSize: null);

        await service.Extract(document, CancellationToken.None);

        files.ContentLoaded.Should().BeTrue();
    }
}
