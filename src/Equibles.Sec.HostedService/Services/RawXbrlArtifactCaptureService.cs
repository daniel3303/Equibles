using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Persists the raw XBRL envelope of a filing at ingest time so the dimensional-fact
/// extractors can read it later without re-downloading the filing. Two complementary
/// artifacts are captured by name: the inline iXBRL primary document (modern filings)
/// and the standalone XBRL <c>.xml</c> instance (older filings). Capture is opt-in
/// (see <see cref="RawXbrlArtifactOptions"/>), idempotent per accession + artifact
/// type, and wrapped so it can never break the surrounding document ingest.
/// </summary>
public class RawXbrlArtifactCaptureService
{
    // Linkbase companions share the instance's base name but carry these suffixes;
    // they are not the instance document and must be excluded when selecting it.
    private static readonly string[] LinkbaseSuffixes =
    [
        "_cal.xml",
        "_def.xml",
        "_lab.xml",
        "_pre.xml",
        "_ref.xml",
    ];

    // EDGAR's rendered financial reports are named R1.xml, R2.xml, …; they are
    // derived views, not the XBRL instance.
    private static readonly Regex RenderedReportPattern = new(
        @"^r\d+\.xml$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private readonly ISecEdgarClient _secEdgarClient;
    private readonly RawFilingArtifactRepository _repository;
    private readonly RawXbrlArtifactOptions _options;
    private readonly ILogger<RawXbrlArtifactCaptureService> _logger;

    public RawXbrlArtifactCaptureService(
        ISecEdgarClient secEdgarClient,
        RawFilingArtifactRepository repository,
        IOptions<RawXbrlArtifactOptions> options,
        ILogger<RawXbrlArtifactCaptureService> logger
    )
    {
        _secEdgarClient = secEdgarClient;
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Captures the enabled raw XBRL envelopes for a filing. Any failure is logged and
    /// swallowed — capture must never interrupt ingest.
    /// </summary>
    public async Task Capture(
        CommonStock company,
        FilingData filing,
        CancellationToken cancellationToken = default
    )
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            if (_options.CaptureInlineIxbrl)
            {
                await CaptureInline(company, filing, cancellationToken);
            }

            if (_options.CaptureStandaloneXbrl)
            {
                await CaptureStandalone(company, filing, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to capture raw XBRL artifacts for {Ticker} {Accession}; ingest continues.",
                company.Ticker,
                filing.AccessionNumber
            );
        }
    }

    private async Task CaptureInline(
        CommonStock company,
        FilingData filing,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(filing.PrimaryDocument))
        {
            return;
        }

        if (await _repository.Exists(filing.AccessionNumber, RawFilingArtifactType.InlineIxbrl))
        {
            return;
        }

        var bytes = await _secEdgarClient.GetDocumentFileBytes(
            filing.Cik,
            filing.AccessionNumber,
            filing.PrimaryDocument,
            cancellationToken
        );

        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        // Only keep documents that actually carry inline XBRL; plain-HTML primary
        // documents (older filings, Forms 3/4/5, …) have no ix:header to extract.
        if (!ContainsInlineXbrl(Encoding.UTF8.GetString(bytes)))
        {
            return;
        }

        await Store(
            company,
            filing.AccessionNumber,
            RawFilingArtifactType.InlineIxbrl,
            filing.PrimaryDocument,
            bytes
        );
    }

    private async Task CaptureStandalone(
        CommonStock company,
        FilingData filing,
        CancellationToken cancellationToken
    )
    {
        if (await _repository.Exists(filing.AccessionNumber, RawFilingArtifactType.StandaloneXbrl))
        {
            return;
        }

        var artifactNames = await _secEdgarClient.GetFilingArtifactNames(
            filing.Cik,
            filing.AccessionNumber,
            cancellationToken
        );

        var instanceName = SelectStandaloneXbrlInstance(artifactNames);
        if (instanceName == null)
        {
            return;
        }

        var bytes = await _secEdgarClient.GetDocumentFileBytes(
            filing.Cik,
            filing.AccessionNumber,
            instanceName,
            cancellationToken
        );

        if (bytes == null || bytes.Length == 0)
        {
            return;
        }

        await Store(
            company,
            filing.AccessionNumber,
            RawFilingArtifactType.StandaloneXbrl,
            instanceName,
            bytes
        );
    }

    private async Task Store(
        CommonStock company,
        string accessionNumber,
        RawFilingArtifactType artifactType,
        string sourceFileName,
        byte[] raw
    )
    {
        var compressed = Compress(raw);

        // The preceding Exists check is a fast-path skip; the unique
        // (AccessionNumber, ArtifactType) index is the authoritative idempotency
        // guard should two document workers race the same filing.
        _repository.Add(
            new RawFilingArtifact
            {
                CommonStockId = company.Id,
                AccessionNumber = accessionNumber,
                ArtifactType = artifactType,
                SourceFileName = sourceFileName,
                Content = compressed,
                UncompressedSize = raw.Length,
                CompressedSize = compressed.Length,
            }
        );
        await _repository.SaveChanges();

        _logger.LogInformation(
            "Captured {ArtifactType} XBRL artifact for {Ticker} {Accession} ({Uncompressed} -> {Compressed} bytes)",
            artifactType,
            company.Ticker,
            accessionNumber,
            raw.Length,
            compressed.Length
        );
    }

    /// <summary>
    /// True when the document carries inline XBRL — the <c>ix</c> namespace declaration
    /// or any element in that namespace.
    /// </summary>
    internal static bool ContainsInlineXbrl(string content)
    {
        return content.Contains("xmlns:ix=", StringComparison.OrdinalIgnoreCase)
            || content.Contains("<ix:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Selects the XBRL instance document from a filing's artifact list: the first
    /// <c>.xml</c> that is neither a linkbase companion, the filing summary, nor a
    /// rendered report. Returns null when the filing has no standalone instance.
    /// </summary>
    internal static string SelectStandaloneXbrlInstance(IEnumerable<string> artifactNames)
    {
        if (artifactNames == null)
        {
            return null;
        }

        foreach (var name in artifactNames)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var lower = name.ToLowerInvariant();
            if (!lower.EndsWith(".xml"))
            {
                continue;
            }

            if (lower == "filingsummary.xml")
            {
                continue;
            }

            if (LinkbaseSuffixes.Any(suffix => lower.EndsWith(suffix)))
            {
                continue;
            }

            if (RenderedReportPattern.IsMatch(lower))
            {
                continue;
            }

            return name;
        }

        return null;
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(raw, 0, raw.Length);
        }
        return output.ToArray();
    }
}
