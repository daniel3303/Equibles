using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>
/// Fills <see cref="FinancialConcept"/> metadata (label, documentation text,
/// debit/credit balance) for one company from its recent filings' MetaLinks
/// artifact. MetaLinks carries every tag the filing uses — standard taxonomies
/// AND the filer's own extension concepts, which the Company Facts API never
/// covers — so this is the authoritative, no-inference source for describing
/// company-specific KPIs. Only concepts that already exist (i.e. have facts)
/// are updated; MetaLinks also lists axes and members we don't ingest, and
/// creating fact-less concept rows would pollute the catalog.
/// </summary>
[Service]
public class ConceptMetadataService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<ConceptMetadataService> _logger;
    private readonly ConceptMetadataOptions _options;

    public ConceptMetadataService(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ILogger<ConceptMetadataService> logger,
        IOptions<ConceptMetadataOptions> options
    )
    {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task ProcessStock(CommonStock stock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(stock.Cik))
            return;

        var accessions = await LoadRecentAccessions(stock, cancellationToken);
        var incoming = new Dictionary<(FactTaxonomy, string), TagMetadata>();
        foreach (var accession in accessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes;
            try
            {
                bytes = await _secEdgarClient.GetDocumentFileBytes(
                    stock.Cik,
                    accession,
                    "MetaLinks.json",
                    cancellationToken
                );
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "MetaLinks download failed for {Ticker} {Accession}, skipping",
                    stock.Ticker,
                    accession
                );
                continue;
            }
            // Empty means 404 — pre-2019 filings have no MetaLinks artifact.
            if (bytes.Length == 0)
                continue;

            try
            {
                // Newest filing first: TryAdd keeps its (current) text when an
                // older filing re-documents the same tag.
                foreach (var (pair, metadata) in ParseMetaLinks(bytes))
                    incoming.TryAdd(pair, metadata);
            }
            catch (Exception ex)
                when (ex is Newtonsoft.Json.JsonException or DecoderFallbackException)
            {
                _logger.LogWarning(
                    ex,
                    "MetaLinks parse failed for {Ticker} {Accession}, skipping",
                    stock.Ticker,
                    accession
                );
            }
        }

        if (incoming.Count > 0)
            await ApplyMetadata(incoming, cancellationToken);
        await StampChecked(stock, cancellationToken);
    }

    // The company's newest distinct filings that produced facts, newest first —
    // the latest 10-K plus recent 10-Qs cover annual-only and quarterly tags.
    private async Task<List<string>> LoadRecentAccessions(
        CommonStock stock,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var factRepository = scope.ServiceProvider.GetRequiredService<FinancialFactRepository>();
        return await factRepository
            .GetByStock(stock)
            .GroupBy(f => f.AccessionNumber)
            .Select(g => new { Accession = g.Key, Filed = g.Max(f => f.FiledDate) })
            .OrderByDescending(a => a.Filed)
            .Take(_options.RecentFilingsPerStock)
            .Select(a => a.Accession)
            .ToListAsync(cancellationToken);
    }

    internal sealed record TagMetadata(string Label, string Description, ConceptBalance? Balance);

    // MetaLinks shape: instance → (per source document) → nsprefix (the filer's
    // own extension prefix) + tag → "{prefix}_{LocalName}" entries carrying the
    // crdr balance and the en-us label/documentation strings.
    internal static IEnumerable<((FactTaxonomy, string) Pair, TagMetadata Metadata)> ParseMetaLinks(
        byte[] bytes
    )
    {
        var root = JObject.Parse(Encoding.UTF8.GetString(bytes));
        if (root["instance"] is not JObject instances)
            yield break;

        foreach (var instanceProperty in instances.Properties())
        {
            if (instanceProperty.Value is not JObject instance)
                continue;
            var filerPrefix = instance["nsprefix"]?.Value<string>();
            if (instance["tag"] is not JObject tags)
                continue;

            foreach (var tagProperty in tags.Properties())
            {
                var separator = tagProperty.Name.IndexOf('_');
                if (separator <= 0 || separator >= tagProperty.Name.Length - 1)
                    continue;
                var prefix = tagProperty.Name[..separator];
                var localName = tagProperty.Name[(separator + 1)..];
                if (!TryMapPrefix(prefix, filerPrefix, out var taxonomy, out var tagPrefix))
                    continue;
                if (tagProperty.Value is not JObject tag)
                    continue;

                var role = tag.SelectToken("lang.en-us.role") as JObject;
                var label = role?["label"]?.Value<string>();
                var documentation = role?["documentation"]?.Value<string>();
                var balance = tag["crdr"]?.Value<string>() switch
                {
                    "credit" => ConceptBalance.Credit,
                    "debit" => ConceptBalance.Debit,
                    _ => (ConceptBalance?)null,
                };
                if (label == null && documentation == null && balance == null)
                    continue;

                var storedTag = tagPrefix == null ? localName : $"{tagPrefix}:{localName}";
                yield return (
                    (taxonomy, storedTag),
                    new TagMetadata(label, documentation, balance)
                );
            }
        }
    }

    // Standard taxonomies map to their enum; the filer's own prefix maps to
    // Custom with the extractor's "{prefix}:{LocalName}" tag shape. Reference
    // taxonomies we don't ingest facts for (ecd, country, currency, …) are
    // skipped — there is no concept row to describe.
    private static bool TryMapPrefix(
        string prefix,
        string filerPrefix,
        out FactTaxonomy taxonomy,
        out string tagPrefix
    )
    {
        tagPrefix = null;
        switch (prefix.ToLowerInvariant())
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
        }
        if (filerPrefix != null && prefix.Equals(filerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            taxonomy = FactTaxonomy.Custom;
            tagPrefix = prefix.ToLowerInvariant();
            return true;
        }
        taxonomy = default;
        return false;
    }

    // Updates only concepts that already exist; text/balance is overwritten
    // when the incoming value is non-empty (the newest filing carries the
    // current taxonomy text) and left alone otherwise.
    private async Task ApplyMetadata(
        Dictionary<(FactTaxonomy, string), TagMetadata> incoming,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var conceptRepository =
            scope.ServiceProvider.GetRequiredService<FinancialConceptRepository>();
        var taxonomies = incoming.Keys.Select(p => p.Item1).Distinct().ToList();
        var tags = incoming.Keys.Select(p => p.Item2).Distinct().ToList();
        var concepts = await conceptRepository
            .GetMatching(taxonomies, tags)
            .ToListAsync(cancellationToken);

        var updated = 0;
        foreach (var concept in concepts)
        {
            if (!incoming.TryGetValue((concept.Taxonomy, concept.Tag), out var metadata))
                continue;
            var changed = false;
            if (!string.IsNullOrEmpty(metadata.Label) && metadata.Label != concept.Label)
            {
                concept.Label =
                    metadata.Label.Length <= 512 ? metadata.Label : metadata.Label[..512];
                changed = true;
            }
            if (
                !string.IsNullOrEmpty(metadata.Description)
                && metadata.Description != concept.Description
            )
            {
                concept.Description = metadata.Description;
                changed = true;
            }
            if (metadata.Balance != null && metadata.Balance != concept.Balance)
            {
                concept.Balance = metadata.Balance;
                changed = true;
            }
            if (changed)
                updated++;
        }
        if (updated > 0)
        {
            await conceptRepository.SaveChanges();
            _logger.LogInformation("Concept metadata updated for {Count} concepts", updated);
        }
    }

    private async Task StampChecked(CommonStock stock, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var status = new FinancialFactsSyncStatus
        {
            CommonStockId = stock.Id,
            LastCheckedAt = DateTime.UtcNow,
            ConceptMetadataCheckedAt = DateTime.UtcNow,
        };
        await dbContext
            .Set<FinancialFactsSyncStatus>()
            .UpsertRange(status)
            .On(s => s.CommonStockId)
            .WhenMatched(
                (existing, incoming) =>
                    new FinancialFactsSyncStatus
                    {
                        ConceptMetadataCheckedAt = incoming.ConceptMetadataCheckedAt,
                    }
            )
            .RunAsync(cancellationToken);
    }
}
