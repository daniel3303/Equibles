using System.Data;
using System.Data.Common;
using System.Text.Json;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.BusinessLogic.Merge;

/// <summary>
/// Folds a duplicate issuer into the issuer that represents the same company, in one transaction:
/// securities, listings and their ids are kept, every reference follows the survivor, and the
/// retired issuer is deleted last so any reference left behind aborts the merge.
/// </summary>
[Service]
public class EquityIssuerMergeManager
{
    public const string IssuerTable = "EquityIssuer";
    public const string IssuerKey = "Id";
    public const string IssuerColumnSuffix = "EquityIssuerId";

    private readonly EquiblesFinancialDbContext _context;
    private readonly EquityIssuerRepository _issuers;
    private readonly IEnumerable<IEquityReferenceRules> _rules;

    public EquityIssuerMergeManager(
        EquiblesFinancialDbContext context,
        EquityIssuerRepository issuers,
        IEnumerable<IEquityReferenceRules> rules
    )
    {
        _context = context;
        _issuers = issuers;
        _rules = rules;
    }

    public EquityReferencePolicy Policy() =>
        new(CommonEquityReferenceRules.All.Concat(_rules.SelectMany(source => source.Rules)));

    /// <summary>Runs the merge; a request that does not execute, or any refusal, rolls everything back.</summary>
    public Task<EquityIssuerMergeReport> Merge(
        EquityIssuerMergeRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (_context.Database.CurrentTransaction != null)
            throw new InvalidOperationException("An issuer merge owns its transaction.");
        if (_context.ChangeTracker.HasChanges())
            throw new InvalidOperationException(
                "An issuer merge needs a context without pending changes."
            );
        return _context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                var report = new EquityIssuerMergeReport
                {
                    RetiredIssuerId = request.RetiredIssuerId,
                    SurvivingIssuerId = request.SurvivingIssuerId,
                };
                await using var transaction = await _context.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                );
                try
                {
                    await MergeCore(request, report, cancellationToken);
                }
                catch (DbException exception)
                {
                    report.Refusals.Add("The database refused the merge: " + exception.Message);
                }
                if (request.Execute && report.IsClean)
                {
                    await transaction.CommitAsync(cancellationToken);
                    report.Executed = true;
                }
                else
                {
                    await transaction.RollbackAsync(cancellationToken);
                    report.Executed = false;
                }
                _context.ChangeTracker.Clear();
                return report;
            });
    }

    private async Task MergeCore(
        EquityIssuerMergeRequest request,
        EquityIssuerMergeReport report,
        CancellationToken cancellationToken
    )
    {
        var retiredId = request.RetiredIssuerId;
        var survivorId = request.SurvivingIssuerId;
        await _context.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout = '15s'",
            cancellationToken
        );
        // Directory identity first, then CUSIP identity: the order every other writer that takes both follows.
        await _issuers.BeginDirectoryIdentityWrite(cancellationToken);
        await _issuers.BeginCusipIdentityWrite(cancellationToken);
        // The retired row is locked against new references, the survivor only against concurrent rewrites,
        // both in id order like every other writer that locks several issuers.
        foreach (var id in new[] { retiredId, survivorId }.Distinct().Order())
        {
            if (id == retiredId)
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM \"EquityIssuer\" WHERE \"Id\" = {retiredId} FOR UPDATE",
                    cancellationToken
                );
            else
                await _issuers.LockIssuersForNoKeyUpdate([survivorId], cancellationToken);
        }

        var issuers = await _context
            .Set<EquityIssuer>()
            .AsNoTracking()
            .Include(issuer => issuer.Securities)
                .ThenInclude(security => security.Listings)
            .Where(issuer => issuer.Id == retiredId || issuer.Id == survivorId)
            .ToListAsync(cancellationToken);
        var retired = issuers.SingleOrDefault(issuer => issuer.Id == retiredId);
        var survivor = issuers.SingleOrDefault(issuer => issuer.Id == survivorId);
        await Validate(request, retired, survivor, report, cancellationToken);
        if (!report.IsClean)
            return;

        // The presentation trigger rejects a security move while the retired presentation selects it.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"EquityIssuerPresentation\" WHERE \"EquityIssuerId\" = {retiredId}",
            cancellationToken
        );
        foreach (var prefix in request.WithdrawnFactPrefixes.Distinct(StringComparer.Ordinal))
        {
            report.WithdrawnFacts += await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"FinancialFact\" WHERE \"EquityIssuerId\" = {retiredId} AND starts_with(\"AccessionNumber\", {prefix})",
                cancellationToken
            );
        }

        var policy = Policy();
        var rekeyer = await EquityReferenceRekeyer.Create(
            _context.Database,
            policy,
            report,
            cancellationToken
        );
        // Trigger-guarded rows are lifted out before the securities move, then re-inserted under the survivor.
        var references = rekeyer
            .IssuerReferences(IssuerTable, IssuerKey, IssuerColumnSuffix)
            .OrderBy(reference => policy.Rule(reference.Table)?.Reinsert == true ? 0 : 1)
            .ToList();
        foreach (var reference in references)
            await rekeyer.Rekey(reference, retiredId, survivorId, cancellationToken);
        if (!report.IsClean)
            return;
        await rekeyer.Reattach(survivorId, cancellationToken);

        // The audit row commits only with the merge, so it records the merge as executed.
        report.Executed = request.Execute;
        _context.Add(
            new EquityIssuerMerge
            {
                RetiredIssuerId = retiredId,
                SurvivingIssuerId = survivorId,
                EvidenceKind = request.EvidenceKind,
                EvidenceSourceRecordIds = request.EvidenceSourceRecordIds.Distinct().ToList(),
                RetiredIssuerJson = JsonSerializer.Serialize(Snapshot(retired)),
                ReportJson = JsonSerializer.Serialize(report),
                MergedBy = request.MergedBy,
                MergedAt = DateTime.UtcNow,
            }
        );
        await _context.SaveChangesAsync(cancellationToken);
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"EquityIssuer\" WHERE \"Id\" = {retiredId}",
            cancellationToken
        );
        await Enrich(survivor, retired, cancellationToken);
    }

    private async Task Validate(
        EquityIssuerMergeRequest request,
        EquityIssuer retired,
        EquityIssuer survivor,
        EquityIssuerMergeReport report,
        CancellationToken cancellationToken
    )
    {
        var refusals = report.Refusals;
        if (request.RetiredIssuerId == request.SurvivingIssuerId)
            refusals.Add("An issuer cannot be merged into itself.");
        if (retired == null || survivor == null)
        {
            refusals.Add("Both issuers must exist.");
            return;
        }
        if (
            string.IsNullOrWhiteSpace(request.EvidenceKind)
            || string.IsNullOrWhiteSpace(request.MergedBy)
        )
            refusals.Add("A merge records its evidence kind and operator.");
        if (request.WithdrawnFactPrefixes.Any(string.IsNullOrWhiteSpace))
            refusals.Add(
                "A withdrawn fact prefix must name a source; a blank one matches every fact."
            );
        if (retired.Cik != null || retired.SecondaryCiks.Count > 0)
            refusals.Add("An SEC filer is never the retired issuer.");
        if (
            retired.LegalEntityIdentifier != null
            && survivor.LegalEntityIdentifier != null
            && retired.LegalEntityIdentifier != survivor.LegalEntityIdentifier
        )
            refusals.Add("The issuers carry different legal entity identifiers.");
        var survivorIsins = survivor
            .Securities.Where(security => security.Isin != null)
            .Select(security => security.Isin)
            .ToHashSet(StringComparer.Ordinal);
        if (
            retired.Securities.Any(security =>
                security.Isin != null && survivorIsins.Contains(security.Isin)
            )
        )
            refusals.Add(
                "Both issuers hold a security with the same ISIN; merge the security first."
            );

        var evidence = request.EvidenceSourceRecordIds.Distinct().ToList();
        var found =
            evidence.Count == 0
                ? 0
                : await _context
                    .Set<EquityDirectorySourceRecord>()
                    .CountAsync(record => evidence.Contains(record.Id), cancellationToken);
        if (evidence.Count == 0 || found != evidence.Count)
            refusals.Add("Every merge cites stored source evidence.");
    }

    // Names stay searchable, and only empty survivor fields are filled; fiscal calendars never transfer.
    private async Task Enrich(EquityIssuer survivor, EquityIssuer retired, CancellationToken token)
    {
        var aliases = survivor.NameAliases.ToList();
        foreach (var name in retired.NameAliases.Prepend(retired.Name))
        {
            if (
                !string.IsNullOrWhiteSpace(name)
                && !string.Equals(name, survivor.Name, StringComparison.OrdinalIgnoreCase)
                && !aliases.Contains(name, StringComparer.OrdinalIgnoreCase)
            )
                aliases.Add(name);
        }
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "EquityIssuer" SET
                "NameAliases" = {aliases},
                "LegalEntityIdentifier" = coalesce("LegalEntityIdentifier", {retired.LegalEntityIdentifier}),
                "Website" = coalesce("Website", {retired.Website}),
                "Description" = coalesce("Description", {retired.Description}),
                "IndustryId" = coalesce("IndustryId", {retired.IndustryId})
            WHERE "Id" = {survivor.Id}
            """,
            token
        );
    }

    private static object Snapshot(EquityIssuer issuer) =>
        new
        {
            issuer.Id,
            issuer.Name,
            issuer.NameAliases,
            issuer.LegalEntityIdentifier,
            issuer.Website,
            issuer.Description,
            issuer.IdentitySourceUrl,
            issuer.IndustryId,
            issuer.FiscalYearEndMonth,
            issuer.FiscalYearEndDay,
            Securities = issuer.Securities.Select(security => new
            {
                security.Id,
                security.Isin,
                security.Cusip,
                Listings = security.Listings.Select(listing => new
                {
                    listing.Id,
                    listing.Ticker,
                    listing.MarketIdentifierCode,
                }),
            }),
        };
}
