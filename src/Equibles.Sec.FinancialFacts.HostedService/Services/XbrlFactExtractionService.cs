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
/// The API stays authoritative for the consolidated (no-dimension) context of
/// <em>standard-taxonomy</em> concepts: for those this extractor persists only
/// facts carrying at least one explicit dimension, so its rows (non-empty
/// <see cref="FinancialFact.DimensionsKey"/>) can never collide with
/// API-sourced rows (empty key) on the natural-key unique index.
/// <em>Filer-extension</em> concepts (<see cref="FactTaxonomy.Custom"/> — the
/// company's own KPI tags like subscriber counts or ARR) never appear in the
/// API at all, so they are persisted at every dimensionality, consolidated
/// context included; classification is by namespace ownership (a concept
/// namespace not hosted by a standards body is the filer's), never by prefix
/// spelling. Their stored tag keeps the QName shape (<c>adbe:Subscribers</c>)
/// so extension concepts from different companies never share a
/// <see cref="FinancialConcept"/> row.
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
    // Version 2: TR2/TR3 zerodash + TR4/TR5 num-comma-decimal(-apos) format
    // coverage in InlineXbrlParser.
    // Version 3: filer-extension (company-specific) concepts persisted under
    // FactTaxonomy.Custom, consolidated contexts included.
    public const int CurrentVersion = 3;

    private const int InsertBatchSize = 1000;

    /// <summary>
    /// Envelopes above this uncompressed size are skipped instead of parsed.
    /// The parsers materialise the whole document in memory (the DOM costs a
    /// large multiple of the source size), so a nine-figure envelope — rare
    /// foreign-issuer filings attach 100+ MB of inline-XBRL exhibits, while
    /// the p99.9 of successfully parsed envelopes is ~21 MB — can OOM the
    /// whole worker process under memory pressure. A skipped document
    /// completes its sweep normally (the caller stamps <see cref="CurrentVersion"/>),
    /// so it is only revisited on a version bump, where the guard re-skips it
    /// before any content is loaded.
    /// </summary>
    internal const long MaxParseableEnvelopeBytes = 50 * 1024 * 1024;

    // Column limits the parsed values must fit (FinancialFact.Unit,
    // FinancialFactDimension.Axis/Member). Facts that exceed them are skipped
    // rather than truncated — a truncated QName would corrupt the key.
    private const int UnitMaxLength = 32;
    private const int QNameMaxLength = 256;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InlineXbrlParser _inlineParser;
    private readonly StandaloneXbrlParser _standaloneParser;
    private readonly IFileManager _fileManager;
    private readonly ILogger<XbrlFactExtractionService> _logger;

    public XbrlFactExtractionService(
        IServiceScopeFactory scopeFactory,
        InlineXbrlParser inlineParser,
        StandaloneXbrlParser standaloneParser,
        IFileManager fileManager,
        ILogger<XbrlFactExtractionService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _inlineParser = inlineParser;
        _standaloneParser = standaloneParser;
        _fileManager = fileManager;
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
        if (document.XbrlUncompressedSize > MaxParseableEnvelopeBytes)
        {
            _logger.LogWarning(
                "Skipping dimensional-fact extraction for document {DocumentId} ({Accession}): "
                    + "envelope is {Size} bytes, above the {Limit}-byte parse ceiling.",
                document.Id,
                document.AccessionNumber,
                document.XbrlUncompressedSize,
                MaxParseableEnvelopeBytes
            );
            return 0;
        }

        var envelope = Encoding.UTF8.GetString(
            GzipCompressor.Decompress(await _fileManager.GetContent(document.XbrlContent))
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
            if (!conceptIds.TryGetValue((candidate.Taxonomy, candidate.Tag), out var conceptId))
                continue;
            facts.Add(BuildFact(document, stock, candidate, conceptId));
            dimensionsByKey.TryAdd(candidate.DimensionsKey, candidate.Fact.Dimensions);
        }

        await BatchPersister.Persist(facts, InsertBatchSize, FlushFacts);
        await PersistDimensions(document, dimensionsByKey, cancellationToken);

        return facts.Count;
    }

    /// <summary>
    /// Keeps the facts this extractor is allowed to persist: a concept in a
    /// standard taxonomy with at least one explicit dimension (the API owns
    /// standard concepts' consolidated context) or a filer-extension concept at
    /// any dimensionality (the API never carries those), and values that fit
    /// their columns.
    /// </summary>
    internal static List<PersistableXbrlFact> SelectPersistable(List<ParsedXbrlFact> parsed)
    {
        var selected = new List<PersistableXbrlFact>();
        foreach (var fact in parsed)
        {
            if (!TryResolveConcept(fact, out var taxonomy, out var tag))
                continue;
            if (taxonomy != FactTaxonomy.Custom && fact.Dimensions.Count == 0)
                continue;
            if (string.IsNullOrEmpty(fact.Unit) || fact.Unit.Length > UnitMaxLength)
                continue;
            if (tag.Length > QNameMaxLength)
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
                    Tag = tag,
                    DimensionsKey = XbrlDimensionsKey.Compute(fact.Dimensions),
                }
            );
        }
        return selected;
    }

    /// <summary>
    /// Resolves a parsed fact's concept identity: standard-taxonomy prefixes
    /// map to their enum arm with the tag as-is; anything else is a
    /// filer-extension concept (<see cref="FactTaxonomy.Custom"/>, tag stored
    /// as <c>prefix:Tag</c>) when its namespace URI is owned by neither a
    /// standards body nor the SEC — namespace ownership is authoritative, the
    /// prefix spelling is not. Facts whose prefix is undeclared or whose
    /// namespace belongs to a reference taxonomy (country, currency, exch, …,
    /// all standards-body-hosted) resolve to nothing and are skipped.
    /// </summary>
    internal static bool TryResolveConcept(
        ParsedXbrlFact fact,
        out FactTaxonomy taxonomy,
        out string tag
    )
    {
        tag = fact.Tag;
        if (string.IsNullOrEmpty(fact.Tag) || string.IsNullOrEmpty(fact.Taxonomy))
        {
            taxonomy = default;
            return false;
        }
        if (TryMapTaxonomy(fact.Taxonomy, out taxonomy))
            return true;
        if (!IsFilerExtensionNamespace(fact.Namespace))
            return false;

        taxonomy = FactTaxonomy.Custom;
        // Prefix casing follows the filer's whim; lowercase it so the same
        // concept lands on one FinancialConcept row across filings.
        //
        // Keying on the PREFIX (not the namespace URI) is a deliberate
        // trade-off: extension namespace URIs are re-dated every filing
        // (http://www.adobe.com/20231201 → …/20241129), so a URI key would
        // split one company's concept history across rows, while the prefix
        // is stable for a filer. Two filers sharing a generic prefix + local
        // name would share a concept row — harmless for values (facts are
        // stock-scoped, and extension concepts carry no SEC label; display
        // labels are humanized from the local name), so meaning cannot leak
        // across companies.
        tag = $"{fact.Taxonomy.ToLowerInvariant()}:{fact.Tag}";
        return true;
    }

    // Registrable domains that host the standard and reference XBRL
    // taxonomies (FASB us-gaap/srt, SEC dei/country/currency/exch/…, IFRS,
    // XBRL spec/utility namespaces, legacy xbrl.us, W3C schema machinery).
    // A concept namespace under any other domain is, per the EDGAR filer
    // manual, the registrant's own extension taxonomy.
    private static readonly string[] StandardsBodyDomains =
    [
        "fasb.org",
        "sec.gov",
        "xbrl.org",
        "ifrs.org",
        "xbrl.us",
        "w3.org",
    ];

    /// <summary>
    /// True when the namespace URI parses and its host is owned by none of the
    /// standards bodies. Unparseable or missing namespaces return false — a
    /// concept that cannot be attributed is skipped, never misfiled.
    /// </summary>
    internal static bool IsFilerExtensionNamespace(string namespaceUri)
    {
        if (string.IsNullOrWhiteSpace(namespaceUri))
            return false;
        if (!Uri.TryCreate(namespaceUri, UriKind.Absolute, out var uri))
            return false;
        var host = uri.Host;
        if (string.IsNullOrEmpty(host))
            return false;
        return !StandardsBodyDomains.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)
        );
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
                    c.Tag,
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
        // Emit the rows in a stable (Taxonomy, Tag) order. Standard concepts
        // (us-gaap:Revenues, …) recur in nearly every filing, so this extractor
        // and the Company Facts importer routinely upsert an overlapping set
        // concurrently. INSERT … ON CONFLICT locks the touched rows in list
        // order; from an unordered HashSet the two writers would grab the same
        // shared rows in opposite orders and deadlock (40P01). A single global
        // order — matched by the importer — makes an ABBA cycle impossible.
        var pairs = persistable.Select(c => (c.Taxonomy, c.Tag)).ToHashSet();
        var concepts = pairs
            .OrderBy(pair => pair.Item1)
            .ThenBy(pair => pair.Item2, StringComparer.Ordinal)
            .Select(pair => new FinancialConcept { Taxonomy = pair.Item1, Tag = pair.Item2 })
            .ToList();

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        // Raw XBRL carries no labels, so this path only ever needs the concept
        // rows to exist — it has nothing to write. DO NOTHING (rather than a
        // Label = existing.Label no-op update) skips taking write locks on the
        // hundreds of already-present shared concept rows, shrinking the window
        // in which this hot table contends with the importer; the importer path
        // still fills labels/descriptions in.
        await dbContext
            .Set<FinancialConcept>()
            .UpsertRange(concepts)
            .On(c => new { c.Taxonomy, c.Tag })
            .NoUpdate()
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

    /// <summary>
    /// A parsed fact admitted for persistence, with its resolved taxonomy, its
    /// storage tag (the raw tag for standard concepts, <c>prefix:Tag</c> for
    /// filer-extension ones) and canonical dimensions key.
    /// </summary>
    internal sealed class PersistableXbrlFact
    {
        public ParsedXbrlFact Fact { get; init; }
        public FactTaxonomy Taxonomy { get; init; }
        public string Tag { get; init; }
        public string DimensionsKey { get; init; }
    }
}
