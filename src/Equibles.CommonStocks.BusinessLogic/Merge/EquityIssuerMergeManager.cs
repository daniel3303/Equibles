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
/// retired issuer is deleted last so any reference left behind aborts the merge. A successor fold
/// (<see cref="EquityIssuerMergeRequest.SuccessorFold"/>) additionally joins the retired issuer's
/// listings into the survivor's listings of the same ticker and continues the survivor under the
/// retired issuer's CIK.
/// </summary>
[Service]
public class EquityIssuerMergeManager
{
    public const string IssuerTable = "EquityIssuer";
    public const string IssuerKey = "Id";
    public const string IssuerColumnSuffix = "EquityIssuerId";
    public const string ListingTable = "EquityListing";
    public const string ListingKey = "Id";
    public const string ListingColumnSuffix = "EquityListingId";

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

    public EquityReferencePolicy ListingPolicy() =>
        new(
            CommonEquityReferenceRules.Listings.Concat(
                _rules.SelectMany(source => source.ListingRules)
            )
        );

    // A successor's own figures come from the same SEC source as the predecessor's, so they follow the fold.
    private EquityReferencePolicy SuccessorPolicy() =>
        new(Policy().Rules.Where(rule => rule.Table != "FinancialFact"));

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
            .Include(issuer => issuer.Presentation)
                .ThenInclude(presentation => presentation.Listing)
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

        var policy = request.SuccessorFold ? SuccessorPolicy() : Policy();
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
        // Only once the issuer-level staging is re-inserted: the listing fold stages rows of its own.
        if (request.SuccessorFold)
        {
            await FoldListings(retired, survivor, report, cancellationToken);
            if (!report.IsClean)
                return;
        }

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
        await Enrich(survivor, retired, request.SuccessorFold, cancellationToken);
        if (request.SuccessorFold)
            await AdoptRegistrant(survivor, retired, cancellationToken);
    }

    // The retired issuer's U.S. listings now belong to the survivor; each one that repeats a survivor
    // listing's ticker folds into that listing, so the ticker keeps one series and one id. The survivor
    // listing takes the live directory designation and the retired listing's current identity (venue,
    // verification), and a retired security emptied by the folds passes its identity (ISIN, CUSIP,
    // registration, source identifiers) to the security that absorbed its listings, the predecessor's
    // CUSIP staying resolvable as an alias.
    private async Task FoldListings(
        EquityIssuer retired,
        EquityIssuer survivor,
        EquityIssuerMergeReport report,
        CancellationToken cancellationToken
    )
    {
        var survivorListings = survivor
            .Securities.SelectMany(security => security.Listings)
            .Where(listing => listing.MarketCountryCode == "US")
            .ToList();
        var folds = new List<(EquityListing From, EquityListing To)>();
        foreach (
            var from in retired
                .Securities.SelectMany(security => security.Listings)
                .Where(listing => listing.MarketCountryCode == "US")
        )
        {
            var matches = survivorListings.Where(listing => listing.Ticker == from.Ticker).ToList();
            if (matches.Count > 1)
            {
                report.Refusals.Add(
                    $"Listing {from.Ticker} matches {matches.Count} survivor listings."
                );
                return;
            }
            if (matches.Count == 0)
                continue;
            var to = matches[0];
            // A listing's denomination is fixed once it records a dividend, so two never blend.
            if (
                from.TradingCurrency != null
                && to.TradingCurrency != null
                && from.TradingCurrency != to.TradingCurrency
            )
            {
                report.Refusals.Add(
                    $"Listing {from.Ticker} trades in {to.TradingCurrency} on the survivor and {from.TradingCurrency} on the retired issuer."
                );
                return;
            }
            // Two retired listings of one ticker would each hand the survivor a different venue identity.
            if (folds.Any(fold => fold.To.Id == to.Id))
            {
                report.Refusals.Add($"Listing {from.Ticker} is held twice by the retired issuer.");
                return;
            }
            folds.Add((from, to));
        }
        var absorbed = new List<(EquitySecurity From, EquitySecurity To)>();
        foreach (
            var security in retired.Securities.Where(security =>
                security.Listings.Count > 0
                && security.Listings.All(listing => folds.Any(fold => fold.From.Id == listing.Id))
            )
        )
        {
            var targets = folds
                .Where(fold => fold.From.EquitySecurityId == security.Id)
                .Select(fold => fold.To.EquitySecurityId)
                .Distinct()
                .ToList();
            if (targets.Count != 1)
            {
                report.Refusals.Add(
                    $"Security {security.Id} folds into {targets.Count} survivor securities."
                );
                return;
            }
            absorbed.Add((security, survivor.Securities.Single(row => row.Id == targets[0])));
        }

        var policy = ListingPolicy();
        var rekeyer = await EquityReferenceRekeyer.Create(
            _context.Database,
            policy,
            report,
            cancellationToken
        );
        var references = rekeyer
            .IssuerReferences(ListingTable, ListingKey, ListingColumnSuffix)
            .OrderBy(reference => policy.Rule(reference.Table)?.Reinsert == true ? 0 : 1)
            .ToList();
        foreach (var (from, to) in folds)
        {
            // Re-inserted dividends must match the listing's denomination, so a survivor listing
            // without one takes the retired listing's first; one that records dividends has one.
            if (to.TradingCurrency == null && from.TradingCurrency != null)
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"EquityListing\" SET \"TradingCurrency\" = {from.TradingCurrency} WHERE \"Id\" = {to.Id}",
                    cancellationToken
                );
            foreach (var reference in references)
                await rekeyer.Rekey(reference, from.Id, to.Id, cancellationToken);
            if (!report.IsClean)
                return;
            await rekeyer.Reattach(to.Id, cancellationToken);
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"EquityListing\" WHERE \"Id\" = {from.Id}",
                cancellationToken
            );
            // The live designation and venue identity pass to the survivor listing, once the retired
            // listing no longer holds the active (venue, ticker) slot; its series changed, so its
            // completion is verified again. A lifecycle whose later listing date follows the earlier
            // delisting was relisted, so the delisting no longer stands.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "EquityListing" SET
                    "Active" = "Active" OR {from.Active},
                    "IsDirectoryListed" = "IsDirectoryListed" OR {from.IsDirectoryListed},
                    "IsReferenceListed" = "IsReferenceListed" OR {from.IsReferenceListed},
                    "ListedOn" = CASE WHEN "ListedOn" IS NULL THEN NULL ELSE LEAST("ListedOn", {from.ListedOn}) END,
                    "DelistedOn" = CASE
                        WHEN "Active" OR {from.Active} THEN NULL
                        WHEN "ListedOn" IS NOT NULL AND GREATEST("DelistedOn", {from.DelistedOn}) < LEAST("ListedOn", {from.ListedOn}) THEN NULL
                        ELSE GREATEST("DelistedOn", {from.DelistedOn}) END,
                    "MarketIdentifierCode" = coalesce({from.MarketIdentifierCode}, "MarketIdentifierCode"),
                    "QuoteUnitMultiplier" = coalesce({from.QuoteUnitMultiplier}, "QuoteUnitMultiplier"),
                    "IdentityState" = GREATEST("IdentityState", {(int)from.IdentityState}),
                    "IdentitySourceUrl" = coalesce({from.IdentitySourceUrl}, "IdentitySourceUrl"),
                    "PriceHistoryBackfilled" = FALSE,
                    "HistoricalPriceBackfillAttemptedAt" = NULL
                WHERE "Id" = {to.Id}
                """,
                cancellationToken
            );
            report.Change(ListingTable, ListingKey).DroppedDuplicates++;
            report.Remaps.Add(new EquityKeyRemap(ListingTable, from.Id, to.Id));
        }
        foreach (var (from, to) in absorbed)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"EquitySecuritySourceIdentifier\" SET \"EquitySecurityId\" = {to.Id} WHERE \"EquitySecurityId\" = {from.Id}",
                cancellationToken
            );
            // Deleted first, so the unique ISIN is free for the security that continues.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"EquitySecurity\" WHERE \"Id\" = {from.Id}",
                cancellationToken
            );
            if (from.Cusip != null && to.Cusip != null && from.Cusip != to.Cusip)
                // Filings cite the retired CUSIP for quarters after a change, so it stays resolvable.
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO "EquityIssuerCusipAlias" ("Id", "EquityIssuerId", "Cusip", "CreationTime")
                    VALUES ({Guid.NewGuid()}, {survivor.Id}, {to.Cusip}, {DateTime.UtcNow})
                    ON CONFLICT ("Cusip") DO NOTHING
                    """,
                    cancellationToken
                );
            if (from.Cusip != null)
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM \"EquityIssuerCusipAlias\" WHERE \"EquityIssuerId\" = {survivor.Id} AND \"Cusip\" = {from.Cusip}",
                    cancellationToken
                );
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "EquitySecurity" SET
                    "Isin" = coalesce({from.Isin}, "Isin"),
                    "Cusip" = coalesce({from.Cusip}, "Cusip"),
                    "SharesOutstanding" = CASE WHEN {from.SharesOutstanding} > 0 THEN {from.SharesOutstanding} ELSE "SharesOutstanding" END,
                    "MarketCapitalization" = CASE WHEN {from.MarketCapitalization} > 0 THEN {from.MarketCapitalization} ELSE "MarketCapitalization" END,
                    "RegistrationType" = CASE WHEN {(int)from.RegistrationType} <> 0 THEN {(int)
                    from.RegistrationType} ELSE "RegistrationType" END,
                    "RegistrationTitle" = coalesce({from.RegistrationTitle}, "RegistrationTitle"),
                    "SecurityType" = CASE WHEN {(int)from.SecurityType} <> 0 THEN {(int)
                    from.SecurityType} ELSE "SecurityType" END,
                    "IdentitySourceUrl" = coalesce({from.IdentitySourceUrl}, "IdentitySourceUrl")
                WHERE "Id" = {to.Id}
                """,
                cancellationToken
            );
        }
        // Any other retired security left without listings goes with its issuer.
        foreach (var security in retired.Securities)
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM "EquitySecurity" WHERE "Id" = {security.Id}
                    AND NOT EXISTS (SELECT 1 FROM "EquityListing" WHERE "EquitySecurityId" = {security.Id})
                """,
                cancellationToken
            );
    }

    // The survivor continues as the successor registrant: its CIK, and its filings from here on, are the
    // retired issuer's, while its own CIK stays attached so the predecessor's filings keep flowing in.
    // The registrant profile is read again under the new CIK.
    private async Task AdoptRegistrant(
        EquityIssuer survivor,
        EquityIssuer retired,
        CancellationToken cancellationToken
    )
    {
        var secondaryCiks = survivor
            .SecondaryCiks.Concat(retired.SecondaryCiks)
            .Append(survivor.Cik)
            .Where(cik => cik != null && cik != retired.Cik)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "EquityIssuer" SET
                "Cik" = {retired.Cik},
                "SecondaryCiks" = {secondaryCiks},
                "EntityType" = coalesce({retired.EntityType}, "EntityType"),
                "Sic" = coalesce({retired.Sic}, "Sic"),
                "SecRegistrantProfileReadOn" = NULL,
                "BdcHistoryReadOn" = NULL,
                "PeriodicReportFileNumber" = NULL
            WHERE "Id" = {survivor.Id}
            """,
            cancellationToken
        );
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
        if (request.SuccessorFold)
        {
            if (retired.Cik == null || survivor.Cik == null)
                refusals.Add("A successor fold joins two SEC filers.");
            var listedTicker = retired.Presentation?.Listing is { MarketCountryCode: "US" } listed
                ? listed.Ticker
                : null;
            if (
                listedTicker == null
                || !survivor
                    .Securities.SelectMany(security => security.Listings)
                    .Any(listing =>
                        listing.MarketCountryCode == "US" && listing.Ticker == listedTicker
                    )
            )
                refusals.Add(
                    "A successor fold needs the retired issuer's listed ticker on the survivor."
                );
        }
        else if (retired.Cik != null || retired.SecondaryCiks.Count > 0)
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
    // A successor fold takes the retired issuer's name, the registrant's current one, and keeps the old
    // name as an alias.
    private async Task Enrich(
        EquityIssuer survivor,
        EquityIssuer retired,
        bool successorFold,
        CancellationToken token
    )
    {
        var name =
            successorFold && !string.IsNullOrWhiteSpace(retired.Name)
                ? retired.Name
                : survivor.Name;
        var aliases = survivor.NameAliases.ToList();
        foreach (var candidate in retired.NameAliases.Prepend(retired.Name).Prepend(survivor.Name))
        {
            if (
                !string.IsNullOrWhiteSpace(candidate)
                && !string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
                && !aliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            )
                aliases.Add(candidate);
        }
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "EquityIssuer" SET
                "Name" = {name},
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
            issuer.Cik,
            issuer.SecondaryCiks,
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
