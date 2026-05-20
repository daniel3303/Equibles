using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Ingests SEC Company Facts (pre-parsed, standardized XBRL) for one company
/// into the structured <see cref="FinancialFact"/> / <see cref="FinancialConcept"/>
/// model. Idempotent: facts are upserted on their natural key so re-running a
/// company is a no-op when nothing new was filed.
/// </summary>
[Service]
public class FinancialFactsImportService
{
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<FinancialFactsImportService> _logger;
    private readonly ErrorReporter _errorReporter;

    public FinancialFactsImportService(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ILogger<FinancialFactsImportService> logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public async Task Import(CommonStock stock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(stock.Cik))
            return;

        CompanyFactsResponse response;
        try
        {
            response = await _secEdgarClient.GetCompanyFacts(stock.Cik);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Company Facts download failed for {Ticker} (CIK {Cik}), skipping this cycle",
                stock.Ticker,
                stock.Cik
            );
            return;
        }

        if (response == null || response.Facts.Count == 0)
        {
            await UpsertSyncStatus(stock, null, cancellationToken);
            return;
        }

        var parsed = ParseFacts(response, stock).ToList();
        if (parsed.Count == 0)
        {
            await UpsertSyncStatus(stock, null, cancellationToken);
            return;
        }

        var maxFiled = parsed.Max(p => p.Filed);

        var lastSeen = await GetLastFiledSeen(stock, cancellationToken);
        if (lastSeen.HasValue && lastSeen.Value >= maxFiled)
        {
            // Nothing filed since the last successful sync — record the check
            // and skip the (expensive) re-upsert of the full history.
            await UpsertSyncStatus(stock, lastSeen, cancellationToken);
            return;
        }

        try
        {
            await PersistFacts(stock, parsed, maxFiled, cancellationToken);
        }
        // Per-company fault isolation (mirrors FtdImportService): one company's
        // failure is reported and skipped so the worker cycle continues for the
        // rest of the universe.
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error importing financial facts for {Ticker} (CIK {Cik})",
                stock.Ticker,
                stock.Cik
            );
            await _errorReporter.Report(
                ErrorSource.FinancialFactsScraper,
                "FinancialFactsImport.Import",
                ex.Message,
                ex.StackTrace,
                $"ticker: {stock.Ticker}, cik: {stock.Cik}"
            );
        }
    }

    private async Task PersistFacts(
        CommonStock stock,
        List<ParsedFact> parsed,
        DateOnly maxFiled,
        CancellationToken cancellationToken
    )
    {
        var conceptIds = await ResolveConcepts(parsed, cancellationToken);
        var documentIds = await LoadDocumentIdsByAccession(stock, cancellationToken);

        var built = parsed
            .Select(p => BuildFact(stock, p, conceptIds, documentIds))
            .Where(f => f != null)
            .ToList();

        var droppedConcepts = parsed.Count - built.Count;
        if (droppedConcepts > 0)
        {
            // A non-zero drop means concept resolution missed a tag it
            // should have inserted — surface it rather than hide it.
            _logger.LogWarning(
                "Dropped {Count} facts with unresolved concepts for {Ticker} (CIK {Cik})",
                droppedConcepts,
                stock.Ticker,
                stock.Cik
            );
        }

        var facts = CollapseToNaturalKey(built);

        // SyncStatus is advanced only here, after a successful persist, so a
        // failure leaves the checkpoint un-advanced and the company is
        // retried in full next cycle.
        await BatchPersister.Persist(facts, InsertBatchSize, FlushFacts);
        await UpsertSyncStatus(stock, maxFiled, cancellationToken);

        _logger.LogInformation(
            "Imported {Count} financial facts for {Ticker} (CIK {Cik})",
            facts.Count,
            stock.Ticker,
            stock.Cik
        );
    }

    private IEnumerable<ParsedFact> ParseFacts(CompanyFactsResponse response, CommonStock stock)
    {
        foreach (var (taxonomyKey, concepts) in response.Facts)
        {
            if (!TryMapTaxonomy(taxonomyKey, out var taxonomy))
                continue;

            foreach (var (tag, concept) in concepts)
            {
                foreach (var (unit, values) in concept.Units)
                {
                    foreach (var value in values)
                    {
                        var fact = TryBuildParsedFact(
                            taxonomy,
                            tag,
                            concept.Label,
                            unit,
                            value,
                            stock
                        );
                        if (fact != null)
                            yield return fact;
                    }
                }
            }
        }
    }

    private static ParsedFact TryBuildParsedFact(
        FactTaxonomy taxonomy,
        string tag,
        string label,
        string unit,
        CompanyFactValue value,
        CommonStock stock
    )
    {
        if (!TryMapFiscalPeriod(value.Fp, out var fiscalPeriod))
            return null;
        if (string.IsNullOrEmpty(value.Accn))
            return null;

        var isInstant = value.Start == null;
        var periodStart = value.Start ?? value.End;
        // Derive (FiscalYear, FiscalPeriod) from the period the fact actually
        // measures — the filing's fy/fp identifies the filing, not each
        // comparable-year value inside it (#982). Resolver returns null when
        // FYE info is missing or the duration shape is unrecognised; the
        // original SEC-supplied identity is the fallback.
        var resolved = FiscalPeriodResolver.Resolve(
            periodStart,
            value.End,
            stock.FiscalYearEndMonth,
            stock.FiscalYearEndDay
        );
        return new ParsedFact
        {
            Taxonomy = taxonomy,
            Tag = tag,
            Label = label,
            Unit = unit,
            PeriodType = isInstant ? FactPeriodType.Instant : FactPeriodType.Duration,
            PeriodStart = periodStart,
            PeriodEnd = value.End,
            Value = value.Val,
            FiscalYear = resolved?.Year ?? value.Fy ?? value.End.Year,
            FiscalPeriod = resolved?.Period ?? fiscalPeriod,
            Form = value.Form,
            Filed = value.Filed,
            Accession = value.Accn,
            Frame = value.Frame,
        };
    }

    /// <summary>
    /// Inserts any missing <see cref="FinancialConcept"/> rows, then returns a
    /// (taxonomy, tag) → id map for the taxonomies present in the payload.
    /// </summary>
    private async Task<Dictionary<(FactTaxonomy, string), Guid>> ResolveConcepts(
        List<ParsedFact> parsed,
        CancellationToken cancellationToken
    )
    {
        var (pairs, concepts) = BuildConceptsForUpsert(parsed);

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

        // Compound write (insert-or-update) — kept out of the repository by
        // design; only update Label when the incoming one is non-empty so a
        // later filing with a missing label can't blank a good one.
        await dbContext
            .Set<FinancialConcept>()
            .UpsertRange(concepts)
            .On(c => new { c.Taxonomy, c.Tag })
            .WhenMatched(
                (existing, incoming) =>
                    new FinancialConcept { Label = incoming.Label ?? existing.Label }
            )
            .RunAsync(cancellationToken);

        var conceptRepository =
            scope.ServiceProvider.GetRequiredService<FinancialConceptRepository>();
        var taxonomies = pairs.Select(p => p.Taxonomy).Distinct().ToList();
        var tags = pairs.Select(p => p.Tag).Distinct().ToList();

        var rows = await conceptRepository
            .GetMatching(taxonomies, tags)
            .Select(c => new
            {
                c.Taxonomy,
                c.Tag,
                c.Id,
            })
            .ToListAsync(cancellationToken);

        return rows.Where(r => pairs.Contains((r.Taxonomy, r.Tag)))
            .ToDictionary(r => (r.Taxonomy, r.Tag), r => r.Id);
    }

    private static (
        HashSet<(FactTaxonomy Taxonomy, string Tag)> Pairs,
        List<FinancialConcept> Concepts
    ) BuildConceptsForUpsert(List<ParsedFact> parsed)
    {
        var pairs = parsed.Select(p => (p.Taxonomy, p.Tag)).ToHashSet();

        // Pre-index labels in one pass so the per-pair lookup is O(1); the
        // prior per-pair scan was O(pairs * parsed) on multi-thousand-fact filings.
        var firstLabelByPair = parsed
            .Where(p => !string.IsNullOrEmpty(p.Label))
            .GroupBy(p => (p.Taxonomy, p.Tag))
            .ToDictionary(g => g.Key, g => g.First().Label);

        var concepts = pairs
            .Select(pair => new FinancialConcept
            {
                Taxonomy = pair.Taxonomy,
                Tag = pair.Tag,
                Label = firstLabelByPair.GetValueOrDefault(pair),
            })
            .ToList();

        return (pairs, concepts);
    }

    private async Task<Dictionary<string, Guid>> LoadDocumentIdsByAccession(
        CommonStock stock,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var documentRepository = scope.ServiceProvider.GetRequiredService<DocumentRepository>();

        var rows = await documentRepository
            .GetByCompany(stock)
            .Where(d => d.AccessionNumber != null)
            .Select(d => new { d.Id, d.AccessionNumber })
            .ToListAsync(cancellationToken);

        var map = new Dictionary<string, Guid>();
        foreach (var row in rows)
            map[row.AccessionNumber] = row.Id;
        return map;
    }

    private static FinancialFact BuildFact(
        CommonStock stock,
        ParsedFact p,
        Dictionary<(FactTaxonomy, string), Guid> conceptIds,
        Dictionary<string, Guid> documentIds
    )
    {
        if (!conceptIds.TryGetValue((p.Taxonomy, p.Tag), out var conceptId))
            return null;

        return new FinancialFact
        {
            CommonStockId = stock.Id,
            FinancialConceptId = conceptId,
            DocumentId = documentIds.TryGetValue(p.Accession, out var docId) ? docId : null,
            Unit = p.Unit,
            PeriodType = p.PeriodType,
            PeriodStart = p.PeriodStart,
            PeriodEnd = p.PeriodEnd,
            Value = p.Value,
            FiscalYear = p.FiscalYear,
            FiscalPeriod = p.FiscalPeriod,
            // SEC emits many form strings outside the known DocumentTypes
            // (NT 10-K, S-1, 485BPOS, …); fold them into Other rather than
            // fabricating untracked DocumentType instances.
            Form = DocumentType.FromDisplayName(p.Form) ?? DocumentType.Other,
            FiledDate = p.Filed,
            AccessionNumber = p.Accession,
            Frame = p.Frame,
        };
    }

    private async Task FlushFacts(List<FinancialFact> items)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

        await dbContext
            .Set<FinancialFact>()
            .UpsertRange(items)
            .On(f => new
            {
                f.CommonStockId,
                f.FinancialConceptId,
                f.Unit,
                f.PeriodStart,
                f.PeriodEnd,
                f.AccessionNumber,
            })
            .WhenMatched(
                (existing, incoming) =>
                    new FinancialFact
                    {
                        Value = incoming.Value,
                        Frame = incoming.Frame,
                        FiledDate = incoming.FiledDate,
                        DocumentId = incoming.DocumentId,
                    }
            )
            .RunAsync();
    }

    private async Task<DateOnly?> GetLastFiledSeen(
        CommonStock stock,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<FinancialFactsSyncStatusRepository>();
        var status = await repo.GetByStock(stock).FirstOrDefaultAsync(cancellationToken);
        return status?.LastFiledDateSeen;
    }

    private async Task UpsertSyncStatus(
        CommonStock stock,
        DateOnly? lastFiledSeen,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

        var status = new FinancialFactsSyncStatus
        {
            CommonStockId = stock.Id,
            LastCheckedAt = DateTime.UtcNow,
            LastFiledDateSeen = lastFiledSeen,
        };

        await dbContext
            .Set<FinancialFactsSyncStatus>()
            .UpsertRange(status)
            .On(s => s.CommonStockId)
            .WhenMatched(
                (existing, incoming) =>
                    new FinancialFactsSyncStatus
                    {
                        LastCheckedAt = incoming.LastCheckedAt,
                        LastFiledDateSeen =
                            incoming.LastFiledDateSeen ?? existing.LastFiledDateSeen,
                    }
            )
            .RunAsync(cancellationToken);
    }

    private static bool TryMapTaxonomy(string key, out FactTaxonomy taxonomy)
    {
        switch (key.ToLowerInvariant())
        {
            case "us-gaap":
                taxonomy = FactTaxonomy.UsGaap;
                return true;
            case "dei":
                taxonomy = FactTaxonomy.Dei;
                return true;
            case "ifrs-full":
                taxonomy = FactTaxonomy.IfrsFull;
                return true;
            case "srt":
                taxonomy = FactTaxonomy.Srt;
                return true;
            case "invest":
                taxonomy = FactTaxonomy.Invest;
                return true;
            default:
                taxonomy = default;
                return false;
        }
    }

    private static bool TryMapFiscalPeriod(string fp, out SecFiscalPeriod fiscalPeriod)
    {
        switch (fp?.ToUpperInvariant())
        {
            case "FY":
                fiscalPeriod = SecFiscalPeriod.FullYear;
                return true;
            case "Q1":
                fiscalPeriod = SecFiscalPeriod.Q1;
                return true;
            case "Q2":
                fiscalPeriod = SecFiscalPeriod.Q2;
                return true;
            case "Q3":
                fiscalPeriod = SecFiscalPeriod.Q3;
                return true;
            case "Q4":
                fiscalPeriod = SecFiscalPeriod.Q4;
                return true;
            default:
                fiscalPeriod = default;
                return false;
        }
    }

    // SEC emits the same (concept, unit, period, accession) tuple more
    // than once (frame vs non-frame duplicates, restatement re-emits).
    // Postgres ON CONFLICT DO UPDATE rejects a batch that targets the
    // same row twice, so collapse to one row per unique-index key,
    // keeping the latest-filed value.
    private static List<FinancialFact> CollapseToNaturalKey(List<FinancialFact> built) =>
        built
            .GroupBy(f =>
                (
                    f.CommonStockId,
                    f.FinancialConceptId,
                    f.Unit,
                    f.PeriodStart,
                    f.PeriodEnd,
                    f.AccessionNumber
                )
            )
            .Select(g => g.OrderByDescending(f => f.FiledDate).First())
            .ToList();

    private sealed class ParsedFact
    {
        public FactTaxonomy Taxonomy { get; init; }
        public string Tag { get; init; }
        public string Label { get; init; }
        public string Unit { get; init; }
        public FactPeriodType PeriodType { get; init; }
        public DateOnly PeriodStart { get; init; }
        public DateOnly PeriodEnd { get; init; }
        public decimal Value { get; init; }
        public int FiscalYear { get; init; }
        public SecFiscalPeriod FiscalPeriod { get; init; }
        public string Form { get; init; }
        public DateOnly Filed { get; init; }
        public string Accession { get; init; }
        public string Frame { get; init; }
    }
}
