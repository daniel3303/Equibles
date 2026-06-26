using Equibles.Core.AutoWiring;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.ReportedStatements;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Reconstructs a filing's as-reported statements from its captured R-file bundle: unpacks the
/// bundle, parses each statement's <c>R#.htm</c> into a structured payload, classifies it into a
/// statement-kind tab, resolves the filing's fiscal identity once (from a duration statement, so
/// the balance sheet shares the filing's period), and replaces the document's existing statements
/// with the freshly parsed set. Purely local (no EDGAR), so a parser-version bump re-derives the
/// corpus from the stored bundles. The caller stamps the parser version and saves.
/// </summary>
[Service]
public class ReportedStatementsParseService
{
    private const string FilingSummaryFileName = "FilingSummary.xml";

    private readonly ReportedFinancialStatementRepository _statementRepository;

    public ReportedStatementsParseService(ReportedFinancialStatementRepository statementRepository)
    {
        _statementRepository = statementRepository;
    }

    /// <summary>
    /// Parses the document's captured bundle and replaces its reconstructed statements. Returns the
    /// number of statements written (0 when the bundle held nothing parseable — the stale set is
    /// still cleared).
    /// </summary>
    public async Task<int> Parse(Document document, CancellationToken cancellationToken)
    {
        var bytes = document.ReportedStatementsContent?.FileContent?.Bytes;
        if (bytes is not { Length: > 0 })
        {
            return 0;
        }

        var files = ReportedStatementsBundle.Unpack(bytes);
        files.TryGetValue(FilingSummaryFileName, out var summaryXml);
        var reports = FilingSummaryParser.StatementReports(summaryXml);

        var parsed = new List<(FilingSummaryReport Report, RFileStatement Statement)>();
        foreach (var report in reports)
        {
            if (!files.TryGetValue(report.HtmlFileName, out var html))
            {
                continue;
            }
            var statement = RFileStatementParser.Parse(html);
            if (!statement.IsEmpty)
            {
                parsed.Add((report, statement));
            }
        }

        // Re-parse is idempotent: clear the document's prior statements, then write the new set.
        await _statementRepository.GetByDocument(document).ExecuteDeleteAsync(cancellationToken);
        if (parsed.Count == 0)
        {
            return 0;
        }

        var (fiscalYear, fiscalPeriod) = ResolveFilingFiscalIdentity(document, parsed);

        var entities = parsed
            .Select(p => Build(document, p.Report, p.Statement, fiscalYear, fiscalPeriod))
            .ToList();
        _statementRepository.AddRange(entities);
        return entities.Count;
    }

    private static ReportedFinancialStatement Build(
        Document document,
        FilingSummaryReport report,
        RFileStatement statement,
        int fiscalYear,
        Equibles.Sec.FinancialFacts.Data.Enums.SecFiscalPeriod fiscalPeriod
    ) =>
        new()
        {
            CommonStockId = document.CommonStockId,
            DocumentId = document.Id,
            AccessionNumber = document.AccessionNumber,
            Kind = ReportedStatementKindClassifier.Classify(report.ShortName, report.LongName),
            RoleUri = report.Role ?? report.HtmlFileName,
            RoleShortName = report.ShortName,
            ReportFileName = report.HtmlFileName,
            IsParenthetical = ReportedStatementKindClassifier.IsParenthetical(report.ShortName),
            FiscalYear = fiscalYear,
            FiscalPeriod = fiscalPeriod,
            PrimaryPeriodEnd = statement.PrimaryPeriodEnd,
            Form = document.DocumentType,
            FiledDate = document.ReportingDate,
            Position = report.Position,
            Currency = statement.Currency,
            Scale = statement.Scale,
            Payload = JsonConvert.SerializeObject(statement.Payload),
        };

    // The filing's fiscal year/period is the same for all its statements. Resolve it once from a
    // duration statement (income / cash flow), whose primary period has a shape the resolver can
    // place against the company's fiscal-year end; an instant (balance sheet) on its own would
    // fall back to a calendar quarter and mislabel a non-calendar-fiscal filer.
    private static (
        int Year,
        Equibles.Sec.FinancialFacts.Data.Enums.SecFiscalPeriod Period
    ) ResolveFilingFiscalIdentity(
        Document document,
        List<(FilingSummaryReport Report, RFileStatement Statement)> parsed
    )
    {
        var anchor =
            parsed.Select(p => p.Statement).FirstOrDefault(s => !s.PrimaryIsInstant)
            ?? parsed.Select(p => p.Statement).OrderByDescending(s => s.PrimaryPeriodEnd).First();

        return XbrlFactExtractionService.ResolveFiscalIdentity(
            anchor.PrimaryPeriodStart,
            anchor.PrimaryPeriodEnd,
            document.CommonStock?.FiscalYearEndMonth,
            document.CommonStock?.FiscalYearEndDay
        );
    }
}
