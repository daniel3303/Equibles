using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Services;

public static class EsefRetainedOriginal
{
    public static async Task<byte[]> ReadEnvelope(
        Document document,
        IFileManager files,
        CancellationToken cancellationToken = default
    )
    {
        var source = await ReadSource(document, files, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var report = EsefReportEnvelope.Discover(source, document.ReportingDate);
        if (
            report.LegalEntityIdentifier != document.Issuer.LegalEntityIdentifier
            || report.PeriodEnd != document.ReportingForDate
        )
            throw new InvalidDataException(
                "Retained report does not match the stored issuer and annual period."
            );
        return EsefReportEnvelope.ForRetrieval(source);
    }

    public static async Task<byte[]> ReadSource(
        Document document,
        IFileManager files,
        CancellationToken token
    )
    {
        if (
            document.DocumentType != DocumentType.EsefAnnualReport
            || document.XbrlType != XbrlType.InlineIxbrl
            || document.XbrlStatus != XbrlCaptureStatus.Captured
            || document.AsFiledHtmlContent == null
            || document.AsFiledHtmlContent.StorageProvider == StorageProvider.FileSystemGzip
            || document.AsFiledHtmlContent.Size
                is not (> 0 and <= EsefReportEnvelope.MaximumSourceBytes)
            || document.AsFiledHtmlUncompressedSize
                is not (> 0 and <= EsefReportEnvelope.MaximumSourceBytes)
        )
            throw new InvalidDataException(
                "The retained original is not a supported captured annual report."
            );

        await using var stream = await files.OpenRead(document.AsFiledHtmlContent);
        var compressed = await ReadBounded(stream, document.AsFiledHtmlContent.Size, token);
        if (
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(compressed))
            != document.AsFiledHtmlContent.ContentHash
        )
            throw new InvalidDataException(
                "Retained original bytes do not match their captured hash."
            );
        if (
            compressed.Length < 18
            || compressed[0] != 0x1f
            || compressed[1] != 0x8b
            || compressed[2] != 8
            || BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length - 4))
                != document.AsFiledHtmlUncompressedSize
        )
            throw new InvalidDataException(
                "Retained original is not a complete captured gzip report."
            );
        using var input = new MemoryStream(compressed, writable: false);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        return await ReadBounded(gzip, document.AsFiledHtmlUncompressedSize.Value, token);
    }

    private static async Task<byte[]> ReadBounded(
        Stream source,
        long expectedLength,
        CancellationToken token
    )
    {
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > expectedLength)
                throw new InvalidDataException("Retained original exceeds its captured length.");
            output.Write(buffer, 0, count);
        }
        if (output.Length != expectedLength)
            throw new InvalidDataException("Retained original length does not match its capture.");
        return output.ToArray();
    }
}
