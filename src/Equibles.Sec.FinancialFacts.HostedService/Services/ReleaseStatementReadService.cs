using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.ReleaseStatements;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Reads one earnings release for the statement figures it states ahead of the periodic report
/// and replaces the release's stored reading. The matcher's inputs are what the company had on
/// file before the release: its consolidated periodic-report facts and the lines of its
/// as-filed primary statements. Purely local, no model and no EDGAR.
/// </summary>
[Service]
public class ReleaseStatementReadService
{
    // A release compares with the same period one year back; the extra months keep every
    // filing that stated that period, so a period on file at two values is seen as such.
    private const int MonthsOnFile = 27;

    private readonly FinancialFactRepository _factRepository;
    private readonly ReportedFinancialStatementRepository _statementRepository;
    private readonly ReleaseStatementReadRepository _readRepository;
    private readonly IFileManager _fileManager;

    public ReleaseStatementReadService(
        FinancialFactRepository factRepository,
        ReportedFinancialStatementRepository statementRepository,
        ReleaseStatementReadRepository readRepository,
        IFileManager fileManager
    )
    {
        _factRepository = factRepository;
        _statementRepository = statementRepository;
        _readRepository = readRepository;
        _fileManager = fileManager;
    }

    /// <summary>Reads the release and stores the reading; returns the figures placed.</summary>
    public async Task<int> Read(Document release, CancellationToken cancellationToken)
    {
        var bytes = await _fileManager.GetContent(release.Content);
        var text = bytes is { Length: > 0 } ? Encoding.UTF8.GetString(bytes) : string.Empty;

        var filed = release.ReportingDate;
        var since = filed.AddMonths(-MonthsOnFile);
        var figures = ReleaseStatementMatcher.Match(
            text,
            filed,
            await FactsOnFile(release.EquityIssuerId, since, filed, cancellationToken),
            await FiledLines(release.EquityIssuerId, since, filed, cancellationToken)
        );

        var stale = await _readRepository
            .GetByDocumentId(release.Id)
            .ToListAsync(cancellationToken);
        _readRepository.Delete(stale);
        _readRepository.Add(
            new ReleaseStatementRead
            {
                EquityIssuerId = release.EquityIssuerId,
                DocumentId = release.Id,
                MatcherVersion = ReleaseStatementRead.CurrentMatcherVersion,
                FigureCount = figures.Count,
                Figures = figures
                    .Select(figure => new ReleaseStatementFigure
                    {
                        EquityIssuerId = release.EquityIssuerId,
                        FinancialConceptId = figure.ConceptId,
                        DocumentId = release.Id,
                        FiledDate = filed,
                        Unit = figure.Unit,
                        PeriodStart = figure.PeriodStart,
                        PeriodEnd = figure.PeriodEnd,
                        Value = figure.Value,
                        FiscalYear = figure.FiscalYear,
                        FiscalPeriod = figure.FiscalPeriod,
                        SourceLine = figure.SourceLine,
                        SourceLabel = figure.SourceLabel,
                    })
                    .ToList(),
            }
        );
        await _readRepository.SaveChanges();
        return figures.Count;
    }

    private async Task<List<OnFileFact>> FactsOnFile(
        Guid issuerId,
        DateOnly since,
        DateOnly before,
        CancellationToken cancellationToken
    ) =>
        await _factRepository
            .GetByIssuerId(issuerId)
            .Where(f =>
                f.DimensionsKey == ""
                && f.FiledDate < before
                && f.PeriodEnd >= since
                && (
                    f.Form == DocumentType.TenQ
                    || f.Form == DocumentType.TenQa
                    || f.Form == DocumentType.TenK
                    || f.Form == DocumentType.TenKa
                )
            )
            .Select(f => new OnFileFact(
                f.FinancialConceptId,
                f.FinancialConcept.Taxonomy,
                f.FinancialConcept.Tag,
                f.Unit,
                f.PeriodStart,
                f.PeriodEnd,
                f.Value,
                f.FiscalYear,
                f.FiscalPeriod,
                f.FinancialConcept.Balance
            ))
            .ToListAsync(cancellationToken);

    private async Task<List<FiledStatementLine>> FiledLines(
        Guid issuerId,
        DateOnly since,
        DateOnly before,
        CancellationToken cancellationToken
    )
    {
        var payloads = await _statementRepository
            .GetByIssuerId(issuerId)
            .Where(s =>
                !s.IsParenthetical
                && s.FiledDate < before
                && s.FiledDate >= since
                && (
                    s.Kind == ReportedStatementKind.Income
                    || s.Kind == ReportedStatementKind.BalanceSheet
                    || s.Kind == ReportedStatementKind.CashFlow
                )
            )
            .Select(s => s.Payload)
            .ToListAsync(cancellationToken);

        return payloads
            .Select(JsonConvert.DeserializeObject<ReportedStatementPayload>)
            .Where(payload => payload != null)
            .SelectMany(payload => payload.Rows)
            .Where(row =>
                !row.IsAbstract
                && !string.IsNullOrEmpty(row.Concept)
                && !string.IsNullOrWhiteSpace(row.Label)
            )
            .Select(row => FiledStatementLine.From(row.Taxonomy, row.Concept, row.Label))
            .Distinct()
            .ToList();
    }
}
