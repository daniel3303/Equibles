using System.Collections.Concurrent;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Captures a filing's as-reported statement bundle from SEC EDGAR: fetches
/// <c>FilingSummary.xml</c>, picks the "Statements" reports it lists, fetches each statement's
/// rendered <c>R#.htm</c> table, and stores them as one gzip bundle on the document. The local
/// parse step (a separate sweep) turns that bundle into <c>ReportedFinancialStatement</c> rows,
/// so this network step runs once. No statements found (no FilingSummary, or no "Statements"
/// reports) is terminal — the filing carries nothing to reconstruct.
/// </summary>
[Service]
public class ReportedStatementsCaptureService
{
    private const string FilingSummaryFileName = "FilingSummary.xml";

    private readonly ISecEdgarClient _secEdgarClient;
    private readonly IFileManager _fileManager;
    private readonly ILogger<ReportedStatementsCaptureService> _logger;

    public ReportedStatementsCaptureService(
        ISecEdgarClient secEdgarClient,
        IFileManager fileManager,
        ILogger<ReportedStatementsCaptureService> logger
    )
    {
        _secEdgarClient = secEdgarClient;
        _fileManager = fileManager;
        _logger = logger;
    }

    /// <summary>
    /// Captures the statement bundle for the document and stamps it onto the entity (the caller
    /// owns SaveChanges). Returns the resulting status: <see cref="XbrlCaptureStatus.Captured"/>
    /// when at least one statement table was stored, otherwise
    /// <see cref="XbrlCaptureStatus.NotPresent"/> (terminal — nothing to reconstruct).
    /// </summary>
    /// <param name="maxParallelFetches">
    /// How many statement R-files to fetch concurrently. Each fetch still funnels through the shared
    /// SEC rate limiter, so this never exceeds the global req/s ceiling — it just keeps the limiter
    /// saturated so this bulk backfill claims a larger share of the budget instead of idling between
    /// serial round-trips. Clamped to at least 1.
    /// </param>
    public async Task<XbrlCaptureStatus> Capture(
        Document document,
        int maxParallelFetches,
        CancellationToken cancellationToken
    )
    {
        var cik = document.CommonStock?.Cik;
        var accession = document.AccessionNumber;
        if (string.IsNullOrWhiteSpace(cik) || string.IsNullOrWhiteSpace(accession))
        {
            // No way to locate the filing on EDGAR (legacy / paper-only row) — terminal.
            return XbrlCaptureStatus.NotPresent;
        }

        var summaryBytes = await _secEdgarClient.GetDocumentFileBytes(
            cik,
            accession,
            FilingSummaryFileName,
            cancellationToken
        );
        if (summaryBytes is not { Length: > 0 })
        {
            return XbrlCaptureStatus.NotPresent;
        }

        var summaryXml = Encoding.UTF8.GetString(summaryBytes);
        var reports = FilingSummaryParser.StatementReports(summaryXml);
        if (reports.Count == 0)
        {
            return XbrlCaptureStatus.NotPresent;
        }

        var files = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        files[FilingSummaryFileName] = summaryXml;

        // A role can be listed twice (primary + parenthetical share an R-file occasionally);
        // fetch each table once. Dedup up front so concurrent fetches don't race on the same file.
        var distinctReports = reports
            .Where(r =>
                !string.Equals(
                    r.HtmlFileName,
                    FilingSummaryFileName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .DistinctBy(r => r.HtmlFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Fetch the statement R-files concurrently. The shared SEC rate limiter still caps total
        // throughput, so parallelism only keeps the limiter busy (a larger share of the budget)
        // rather than issuing one serial request at a time.
        await Parallel.ForEachAsync(
            distinctReports,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, maxParallelFetches),
                CancellationToken = cancellationToken,
            },
            async (report, ct) =>
            {
                var bytes = await _secEdgarClient.GetDocumentFileBytes(
                    cik,
                    accession,
                    report.HtmlFileName,
                    ct
                );
                if (bytes is { Length: > 0 })
                {
                    files[report.HtmlFileName] = Encoding.UTF8.GetString(bytes);
                }
                else
                {
                    _logger.LogWarning(
                        "As-reported statement table {File} missing for {Accession}; skipping it.",
                        report.HtmlFileName,
                        accession
                    );
                }
            }
        );

        // Only FilingSummary survived (every R-file 404'd) — nothing usable to parse.
        if (files.Count <= 1)
        {
            return XbrlCaptureStatus.NotPresent;
        }

        var (compressed, uncompressedSize) = ReportedStatementsBundle.Pack(files);
        var file = await _fileManager.SaveInternalFile(
            compressed,
            accession,
            "gz",
            "application/gzip",
            storage: Equibles.Media.Data.Models.StorageProvider.FileSystem
        );

        document.ReportedStatementsContent = file;
        document.ReportedStatementsUncompressedSize = uncompressedSize;
        // A fresh bundle invalidates any earlier parse — let the parse sweep re-derive it.
        document.ReportedStatementsParseVersion = 0;
        return XbrlCaptureStatus.Captured;
    }
}
