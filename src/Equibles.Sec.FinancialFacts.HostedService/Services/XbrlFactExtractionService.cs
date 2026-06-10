using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Worker;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Extracts <em>dimensional</em> financial facts from a document's captured raw
/// XBRL envelope and persists them as <see cref="FinancialFact"/> rows with
/// <see cref="FinancialFactDimension"/> children — the segment / geography /
/// product cuts (e.g. <c>srt:ProductOrServiceAxis</c> → <c>aapl:IPhoneMember</c>)
/// the SEC Company Facts API drops (see #877).
///
/// <para>
/// The API stays authoritative for the consolidated (no-dimension) context:
/// this extractor persists facts that carry at least one explicit dimension,
/// so its rows (non-empty <see cref="FinancialFact.DimensionsKey"/>) can never
/// collide with API-sourced rows (empty key) on the natural-key unique index.
/// Filer-extension <em>members</em> ride through as QName strings; facts whose
/// <em>concept</em> lives in a filer-extension taxonomy are skipped until
/// <see cref="FinancialConcept"/> can represent them (follow-up to #877).
/// </para>
///
/// <para>
/// Callers own selection and bookkeeping (<c>Document.XbrlFactsVersion</c> /
/// <c>XbrlFactsAttempts</c>); this service is a pure parse-and-persist step and
/// throws on persistence failures so the caller can count the attempt.
/// </para>
/// </summary>
[Service]
public class XbrlFactExtractionService
{
    /// <summary>
    /// Stamped onto <c>Document.XbrlFactsVersion</c> after a successful
    /// extraction. Bump to reprocess the whole captured corpus after an
    /// extractor behavior change.
    /// </summary>
    public const int CurrentVersion = 1;

    private const int InsertBatchSize = 1000;

    // Column limits the parsed values must fit (FinancialFact.Unit,
    // FinancialFactDimension.Axis/Member). Facts that exceed them are skipped
    // rather than truncated — a truncated QName would corrupt the key.
    private const int UnitMaxLength = 32;
    private const int QNameMaxLength = 256;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InlineXbrlParser _inlineParser;
    private readonly StandaloneXbrlParser _standaloneParser;
    private readonly ILogger<XbrlFactExtractionService> _logger;

    public XbrlFactExtractionService(
        IServiceScopeFactory scopeFactory,
        InlineXbrlParser inlineParser,
        StandaloneXbrlParser standaloneParser,
        ILogger<XbrlFactExtractionService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _inlineParser = inlineParser;
        _standaloneParser = standaloneParser;
        _logger = logger;
    }

    /// <summary>
    /// Parses the document's captured envelope and upserts its dimensional
    /// facts. Returns the number of facts persisted. Expects
    /// <c>document.XbrlContent</c> (and its content bytes) to be loadable and
    /// <c>document.CommonStock</c> to be set.
    /// </summary>
    public async Task<int> Extract(Document document, CancellationToken cancellationToken)
    {
        if (document.XbrlStatus != XbrlCaptureStatus.Captured || document.XbrlContent == null)
            return 0;
        // The natural key requires an accession; documents without one are
        // legacy/paper rows the capture path never marks Captured anyway.
        if (string.IsNullOrEmpty(document.AccessionNumber))
            return 0;

        var envelope = Encoding.UTF8.GetString(
            GzipCompressor.Decompress(document.XbrlContent.FileContent.Bytes)
        );

        var parsed =
            document.XbrlType == XbrlType.StandaloneXbrl
                ? _standaloneParser.Parse(envelope)
                : _inlineParser.Parse(envelope);

        var persistable = CollapseToNaturalKey(SelectPersistable(parsed));
        if (persistable.Count == 0)
            return 0;

        var conceptIds = await ResolveConcepts(persistable, cancellationToken);

        var stock = document.CommonStock;
        var facts = new List<FinancialFact>();
        var dimensionsByKey = new Dictionary<string, List<ParsedXbrlDimension>>(
            StringComparer.Ordinal
        );
        foreach (var candidate in persistable)
        {
            if (
                !conceptIds.TryGetValue((candidate.Taxonomy, candidate.Fact.Tag), out var conceptId)
            )
                continue;
            facts.Add(BuildFact(document, stock, candidate, conceptId));
            dimensionsByKey.TryAdd(candidate.DimensionsKey, candidate.Fact.Dimensions);
        }

        await BatchPersister.Persist(facts, InsertBatchSize, FlushFacts);
        await PersistDimensions(document, dimensionsByKey, cancellationToken);

        return facts.Count;
    }

    /// <summary>
    /// Keeps the facts this extractor is allowed to persist: at least one
    /// explicit dimension (the API owns the consolidated context), a concept
    /// in a known taxonomy, and values that fit their columns.
    /// </summary>
    internal static List<PersistableXbrlFact> SelectPersistable(List<ParsedXbrlFact> parsed)
    {
        var selected = new List<PersistableXbrlFact>();
        foreach (var fact in parsed)
        {
            if (fact.Dimensions.Count == 0)
                continue;
            if (!TryMapTaxonomy(fact.Taxonomy, out var taxonomy))
                continue;
            if (string.IsNullOrEmpty(fact.Unit) || fact.Unit.Length > UnitMaxLength)
                continue;
            if (string.IsNullOrEmpty(fact.Tag) || fact.Tag.Length > QNameMaxLength)
                continue;
            if (
                fact.Dimensions.Any(d =>
                    string.IsNullOrEmpty(d.Axis)
                    || string.IsNullOrEmpty(d.Member)
                    || d.Axis.Length > QNameMaxLength
                    || d.Member.Length > QNameMaxLength
                )
            )
                continue;

            selected.Add(
                new PersistableXbrlFact
                {
                    Fact = fact,
                    Taxonomy = taxonomy,
                    DimensionsKey = XbrlDimensionsKey.Compute(fact.Dimensions),
                }
            );
        }
        return selected;
    }

    /// <summary>
    /// One row per natural-key slot. Filings routinely render the same fact
    /// more than once (cover page vs statement vs notes); when duplicates
    /// disagree on precision, keep the most precise rendering (highest XBRL
    /// <c>decimals</c>).
    /// </summary>
    internal static List<PersistableXbrlFact> CollapseToNaturalKey(
        List<PersistableXbrlFact> candidates
    ) =>
        candidates
            .GroupBy(c =>
                (
                    c.Taxonomy,
                    c.Fact.Tag,
                    c.Fact.Unit,
                    c.Fact.PeriodStart,
                    c.Fact.PeriodEnd,
                    c.DimensionsKey
                )
            )
            .Select(g => g.OrderByDescending(c => c.Fact.Decimals ?? int.MinValue).First())
            .ToList();

    /// <summary>
    /// Fiscal identity for a parsed period. Unlike the Company Facts API, raw
    /// XBRL carries no fy/fp identity, so resolve from the company's fiscal
    /// year end; when that metadata is missing, fall back to a calendar
    /// approximation (annual-length durations → FY, anything else → the
    /// calendar quarter of the period end) — the same spirit as the API path's
    /// fallback to the SEC-supplied filing identity.
    /// </summary>
    internal static (int Year, SecFiscalPeriod Period) ResolveFiscalIdentity(
        DateOnly periodStart,
        DateOnly periodEnd,
        int? fiscalYearEndMonth,
        int? fiscalYearEndDay
    )
    {
        var resolved = FiscalPeriodResolver.Resolve(
            periodStart,
            periodEnd,
            fiscalYearEndMonth,
            fiscalYearEndDay
        );
        if (resolved != null)
            return resolved.Value;

        var durationDays = periodEnd.DayNumber - periodStart.DayNumber;
        if (durationDays >= 350 && durationDays <= 380)
            return (periodEnd.Year, SecFiscalPeriod.FullYear);

        var quarter = (periodEnd.Month - 1) / 3 + 1;
        var period = quarter switch
        {
            1 => SecFiscalPeriod.Q1,
            2 => SecFiscalPeriod.Q2,
            3 => SecFiscalPeriod.Q3,
            _ => SecFiscalPeriod.Q4,
        };
        return (periodEnd.Year, period);
    }

    private static FinancialFact BuildFact(
        Document document,
        CommonStock stock,
        PersistableXbrlFact candidate,
        Guid conceptId
    )
    {
        var fact = candidate.Fact;
        var (fiscalYear, fiscalPeriod) = ResolveFiscalIdentity(
            fact.PeriodStart,
            fact.PeriodEnd,
            stock.FiscalYearEndMonth,
            stock.FiscalYearEndDay
        );

        return new FinancialFact
        {
            CommonStockId = stock.Id,
            FinancialConceptId = conceptId,
            DocumentId = document.Id,
            Unit = fact.Unit,
            PeriodType = fact.IsInstant ? FactPeriodType.Instant : FactPeriodType.Duration,
            PeriodStart = fact.PeriodStart,
            PeriodEnd = fact.PeriodEnd,
            Value = fact.Value,
            FiscalYear = fiscalYear,
            FiscalPeriod = fiscalPeriod,
            Form = document.DocumentType,
            FiledDate = document.ReportingDate,
            AccessionNumber = document.AccessionNumber,
            DimensionsKey = candidate.DimensionsKey,
        };
    }

    /// <summary>
    /// Inserts any missing <see cref="FinancialConcept"/> rows (no labels —
    /// raw XBRL carries none; the API import fills them in later without
    /// blanking, see its WhenMatched), then returns a (taxonomy, tag) → id map.
    /// </summary>
    private async Task<Dictionary<(FactTaxonomy, string), Guid>> ResolveConcepts(
        List<PersistableXbrlFact> persistable,
        CancellationToken cancellationToken
    )
    {
        var pairs = persistable.Select(c => (c.Taxonomy, c.Fact.Tag)).ToHashSet();
        var concepts = pairs
            .Select(pair => new FinancialConcept { Taxonomy = pair.Item1, Tag = pair.Item2 })
            .ToList();

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

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
        var taxonomies = pairs.Select(p => p.Item1).Distinct().ToList();
        var tags = pairs.Select(p => p.Item2).Distinct().ToList();

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

    private async Task FlushFacts(List<FinancialFact> items)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        await dbContext
            .Set<FinancialFact>()
            .UpsertRange(items)
            // Must name the unique index's full column list (including
            // DimensionsKey) or Postgres can't infer the ON CONFLICT target.
            .On(f => new
            {
                f.CommonStockId,
                f.FinancialConceptId,
                f.Unit,
                f.PeriodStart,
                f.PeriodEnd,
                f.AccessionNumber,
                f.DimensionsKey,
            })
            .WhenMatched(
                (existing, incoming) =>
                    new FinancialFact
                    {
                        Value = incoming.Value,
                        FiledDate = incoming.FiledDate,
                        DocumentId = incoming.DocumentId,
                    }
            )
            .RunAsync();
    }

    /// <summary>
    /// Attaches the (axis, member) child rows to the just-upserted facts. Fact
    /// ids are re-read by document + dimensions key because an upsert that hit
    /// an existing row keeps that row's id, not the incoming one. Dimension
    /// sets are immutable for a given key, so existing children are left
    /// untouched.
    /// </summary>
    private async Task PersistDimensions(
        Document document,
        Dictionary<string, List<ParsedXbrlDimension>> dimensionsByKey,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var persistedFacts = await dbContext
            .Set<FinancialFact>()
            .AsNoTracking()
            .Where(f => f.DocumentId == document.Id && f.DimensionsKey != "")
            .Select(f => new { f.Id, f.DimensionsKey })
            .ToListAsync(cancellationToken);

        var rows = new List<FinancialFactDimension>();
        foreach (var fact in persistedFacts)
        {
            if (!dimensionsByKey.TryGetValue(fact.DimensionsKey, out var dimensions))
                continue;
            rows.AddRange(
                dimensions.Select(d => new FinancialFactDimension
                {
                    FinancialFactId = fact.Id,
                    Axis = d.Axis,
                    Member = d.Member,
                })
            );
        }
        if (rows.Count == 0)
            return;

        foreach (var batch in rows.Chunk(InsertBatchSize))
        {
            await dbContext
                .Set<FinancialFactDimension>()
                .UpsertRange(batch)
                .On(d => new
                {
                    d.FinancialFactId,
                    d.Axis,
                    d.Member,
                })
                .NoUpdate()
                .RunAsync(cancellationToken);
        }
    }

    // Mirrors FinancialFactsImportService.TryMapTaxonomy (kept private there;
    // its arms are reflection-pinned by tests). XBRL prefixes use the same
    // wire spelling as the Company Facts API's top-level keys.
    private static bool TryMapTaxonomy(string prefix, out FactTaxonomy taxonomy)
    {
        switch (prefix?.ToLowerInvariant())
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

    /// <summary>A parsed fact admitted for persistence, with its resolved taxonomy and canonical dimensions key.</summary>
    internal sealed class PersistableXbrlFact
    {
        public ParsedXbrlFact Fact { get; init; }
        public FactTaxonomy Taxonomy { get; init; }
        public string DimensionsKey { get; init; }
    }
}
