using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Exceptions;
using Equibles.Messaging.Contracts.CommonStocks;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.BusinessLogic;

[Service]
public class CommonStockManager
{
    private readonly CommonStockRepository _commonStockRepository;
    private readonly IBus _bus;

    public CommonStockManager(CommonStockRepository commonStockRepository, IBus bus)
    {
        _commonStockRepository = commonStockRepository;
        _bus = bus;
    }

    /// <summary>
    /// Records CUSIPs a stock USED to trade under, without touching its current one.
    /// <para>
    /// <see cref="SetCusip"/> only ever captures a retirement it witnesses live, so a
    /// CUSIP change that happened before this pipeline first ran leaves no alias — and
    /// every 13F line filed under the retired value stays unmappable forever. AMC's
    /// pre-2023-reverse-split 00165C104 is the shape: `GetTopHolders(AMC, 2022-12-31)`
    /// answered with 4 institutions holding 845 shares, against 274 holding 102M a year
    /// later. The cliff is the CUSIP change, not an ownership event.
    /// </para>
    /// <para>
    /// Callers must have established that the CUSIP belongs to this issuer. Aliases the
    /// table has already claimed are left with their first owner (one CUSIP identifies
    /// one security, ever), so a re-run records nothing and publishes nothing.
    /// </para>
    /// <para>
    /// Recording one publishes <see cref="StockCusipChanged"/> for the same reason
    /// <see cref="SetCusip"/> does: quarterly 13F data sets already marked processed hold
    /// no holdings for lines filed under the newly-mapped CUSIP, and the consumer clears
    /// that ledger so the Holdings worker re-imports them. A burst collapses to a no-op
    /// once cleared, so a sweep recording many aliases costs one invalidation.
    /// </para>
    /// </summary>
    public async Task<int> RecordRetiredCusipAliases(
        CommonStock commonStock,
        IEnumerable<string> retiredCusips
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);
        ArgumentNullException.ThrowIfNull(retiredCusips);

        var candidates = retiredCusips
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Where(c => !string.Equals(c, commonStock.Cusip, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var alreadyRecorded = await _commonStockRepository
            .GetCusipAliases()
            .Where(a => candidates.Contains(a.Cusip.ToUpper()))
            .Select(a => a.Cusip)
            .ToListAsync();
        var taken = new HashSet<string>(alreadyRecorded, StringComparer.OrdinalIgnoreCase);
        // A CUSIP recorded as a sibling LISTING is a different security's current identity —
        // aliasing it would outrank the listing at resolution time and merge the two classes'
        // positions into one row. One CUSIP identifies one security, in both directions.
        taken.UnionWith(
            await _commonStockRepository
                .GetListedCusips()
                .Where(l => candidates.Contains(l.Cusip.ToUpper()))
                .Select(l => l.Cusip)
                .ToListAsync()
        );

        var recorded = 0;
        foreach (var cusip in candidates.Where(c => !taken.Contains(c)))
        {
            _commonStockRepository.AddCusipAlias(
                new CommonStockCusipAlias { CommonStockId = commonStock.Id, Cusip = cusip }
            );
            recorded++;
        }

        if (recorded == 0)
        {
            return 0;
        }

        await _commonStockRepository.SaveChanges();

        // Root bus, after the write commits — same reasoning as SetCusip: this flow only
        // saves the financial context, so a bus outbox on another context would capture
        // the publish and never deliver it.
        await _bus.Publish(
            new StockCusipChanged(commonStock.Id, commonStock.Ticker, null, commonStock.Cusip)
        );

        return recorded;
    }

    /// <summary>
    /// Records the CUSIPs of a stock's OTHER listed securities — the sibling share
    /// classes, units, and fund series named in <see cref="CommonStock.SecondaryTickers"/> —
    /// as <see cref="CommonStockListedCusip"/> rows keyed to the exact listed ticker.
    /// <para>
    /// Distinct from <see cref="RecordRetiredCusipAliases"/> on purpose: an alias is a
    /// retired identity of the PRIMARY security, while these are the current identities
    /// of DIFFERENT securities sharing the filer row. The holdings lane resolves a 13F
    /// line filed under one of these CUSIPs to (stock, listed ticker), so the position
    /// imports as its own class instead of being dropped — or worse, merged into the
    /// primary class's row.
    /// </para>
    /// <para>
    /// Callers must have established the pairing from authoritative symbol+CUSIP data.
    /// A CUSIP already claimed anywhere — as a primary, an alias, or another listing —
    /// keeps its first owner (one CUSIP identifies one security, ever). Candidates whose
    /// ticker is not currently one of the stock's secondary tickers are skipped: the
    /// primary's CUSIP identity lives on the stock row and the alias table alone.
    /// </para>
    /// <para>
    /// Recording anything publishes <see cref="StockCusipChanged"/> for the same reason
    /// the alias recorder does: data sets already marked processed hold no rows for
    /// lines filed under the newly-resolvable CUSIP, and the consumer clears that ledger
    /// so the Holdings worker re-imports them. A burst collapses to a no-op once cleared.
    /// </para>
    /// </summary>
    public async Task<int> RecordListedTickerCusips(
        CommonStock commonStock,
        IReadOnlyCollection<(string ListedTicker, string Cusip)> candidates
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);
        ArgumentNullException.ThrowIfNull(candidates);

        var secondaryTickers = new HashSet<string>(
            commonStock.SecondaryTickers ?? [],
            StringComparer.OrdinalIgnoreCase
        );
        var cleaned = candidates
            .Where(c =>
                !string.IsNullOrWhiteSpace(c.ListedTicker) && !string.IsNullOrWhiteSpace(c.Cusip)
            )
            .Select(c => (Ticker: c.ListedTicker.Trim(), Cusip: c.Cusip.Trim().ToUpperInvariant()))
            // Store the SecondaryTickers-side spelling, not the caller's: every consumer joins
            // on this string, and one canonical spelling per listing keeps those joins exact.
            .Select(c =>
                secondaryTickers.TryGetValue(c.Ticker, out var canonical)
                    ? (Ticker: canonical, c.Cusip)
                    : c
            )
            .Where(c =>
                secondaryTickers.Contains(c.Ticker)
                && !string.Equals(c.Cusip, commonStock.Cusip, StringComparison.OrdinalIgnoreCase)
            )
            // A CUSIP offered under TWO listed tickers is contradictory feed data; keeping an
            // arbitrary pairing would price the class from the other sibling's series. Drop it —
            // the same refusal the sweep applies to ambiguous symbols.
            .GroupBy(c => c.Cusip, StringComparer.OrdinalIgnoreCase)
            .Where(g =>
                g.Select(c => c.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            )
            .Select(g => g.First())
            .ToList();
        if (cleaned.Count == 0)
        {
            return 0;
        }

        var candidateCusips = cleaned.Select(c => c.Cusip).ToList();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        taken.UnionWith(
            await _commonStockRepository
                .GetListedCusips()
                .Where(l => candidateCusips.Contains(l.Cusip.ToUpper()))
                .Select(l => l.Cusip)
                .ToListAsync()
        );
        taken.UnionWith(
            await _commonStockRepository
                .GetCusipAliases()
                .Where(a => candidateCusips.Contains(a.Cusip.ToUpper()))
                .Select(a => a.Cusip)
                .ToListAsync()
        );
        taken.UnionWith(
            await _commonStockRepository
                .GetAll()
                .Where(cs => cs.Cusip != null && candidateCusips.Contains(cs.Cusip.ToUpper()))
                .Select(cs => cs.Cusip)
                .ToListAsync()
        );

        var recorded = 0;
        foreach (var candidate in cleaned.Where(c => !taken.Contains(c.Cusip)))
        {
            _commonStockRepository.AddListedCusip(
                new CommonStockListedCusip
                {
                    CommonStockId = commonStock.Id,
                    ListedTicker = candidate.Ticker,
                    Cusip = candidate.Cusip,
                }
            );
            recorded++;
        }

        if (recorded == 0)
        {
            return 0;
        }

        await _commonStockRepository.SaveChanges();

        // Root bus, after the write commits — same reasoning as SetCusip: this flow only
        // saves the financial context, so a bus outbox on another context would capture
        // the publish and never deliver it.
        await _bus.Publish(
            new StockCusipChanged(commonStock.Id, commonStock.Ticker, null, commonStock.Cusip)
        );

        return recorded;
    }

    /// <summary>
    /// Sets a stock's CUSIP. When the value actually changes, publishes
    /// <see cref="StockCusipChanged"/> after SaveChanges so the Holdings module can
    /// backfill quarterly 13F data sets that were processed while this stock
    /// was still unresolvable. A no-op change publishes nothing.
    /// <para>
    /// Replacing a non-null CUSIP (an issuer-level CUSIP change) also records the
    /// retired value as a <see cref="CommonStockCusipAlias"/>. Filings keep
    /// referencing the old CUSIP — laggard 13F filers for a quarter or two, and
    /// historical data sets forever — so import-time resolution must keep mapping
    /// it to this stock. Without the alias, the backfill triggered by the change
    /// would silently drop old-CUSIP lines wherever a restatement amendment
    /// deletes and re-inserts a quarter.
    /// </para>
    /// <para>
    /// This is a financial-domain event, so it publishes via the root
    /// <see cref="IBus"/> rather than the scoped <c>IPublishEndpoint</c>. A host
    /// that enables a bus outbox on a different context (e.g. the commercial
    /// customer database) would otherwise capture this publish into that context
    /// and never deliver it, since this flow only saves the financial context.
    /// The consumer is idempotent; a publish lost after the save committed is
    /// not retried here (the next resolve sees the stored value and no-ops), but
    /// the consumer's ledger clear is global, so any later
    /// <see cref="StockCusipChanged"/> from any stock re-imports the missed
    /// data sets and heals the gap.
    /// </para>
    /// </summary>
    public async Task SetCusip(CommonStock commonStock, string cusip)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (string.Equals(commonStock.Cusip, cusip, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousCusip = commonStock.Cusip;

        // The alias table enforces one CUSIP → one stock, ever (global unique
        // index): a retired CUSIP already recorded — even for another stock —
        // is left with its first owner rather than reassigned. The existence
        // check is case-insensitive so a case-variant CUSIP can't slip past
        // the index as a duplicate row.
        var normalizedPrevious = previousCusip?.ToUpperInvariant();
        if (
            previousCusip != null
            && !await _commonStockRepository
                .GetCusipAliases()
                .AnyAsync(a => a.Cusip.ToUpper() == normalizedPrevious)
        )
        {
            _commonStockRepository.AddCusipAlias(
                new CommonStockCusipAlias { CommonStockId = commonStock.Id, Cusip = previousCusip }
            );
        }

        commonStock.Cusip = cusip;

        await _commonStockRepository.SaveChanges();

        // Publish via the root bus (bypasses any bus outbox) after the write commits.
        await _bus.Publish(
            new StockCusipChanged(commonStock.Id, commonStock.Ticker, previousCusip, cusip)
        );
    }

    /// <summary>
    /// Attaches an additional CIK to a stock — a predecessor registrant after a holdco
    /// reorganisation (Exxon's 2026 reorg moved XOM to a new CIK and left the entire
    /// filing and fact history on the old one, GH-7041) or a co-registrant subsidiary.
    /// Operator-driven: no SEC feed states successorship in structured form, so the
    /// association is a human decision; everything downstream is automatic — filing
    /// discovery and the document scraper enumerate every attached CIK each sweep, and
    /// the published <see cref="StockSecondaryCikAttached"/> resets the financial-facts
    /// checkpoint so the predecessor's full XBRL history imports on the next cycle.
    /// Returns null on success, otherwise a reason the attachment was refused.
    /// </summary>
    public async Task<string> AttachSecondaryCik(CommonStock commonStock, string cik)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        var normalized = NormalizeCik(cik);
        if (normalized == null)
        {
            return $"'{cik}' is not a valid CIK — expected up to ten digits.";
        }
        if (normalized == NormalizeCik(commonStock.Cik))
        {
            return $"CIK {normalized} is already this stock's primary CIK.";
        }
        if (commonStock.SecondaryCiks.Contains(normalized))
        {
            return $"CIK {normalized} is already attached to this stock.";
        }

        // One CIK belongs to one stock, ever — attaching a CIK another row owns
        // (as primary or secondary) would merge two companies' filings.
        var owner = await _commonStockRepository
            .GetAll()
            .Where(cs => cs.Cik == normalized || cs.SecondaryCiks.Contains(normalized))
            .Select(cs => new { cs.Ticker })
            .FirstOrDefaultAsync();
        if (owner != null)
        {
            return $"CIK {normalized} already belongs to {owner.Ticker}.";
        }

        commonStock.SecondaryCiks = [.. commonStock.SecondaryCiks, normalized];
        await _commonStockRepository.SaveChanges();

        // Publish via the root bus (bypasses any bus outbox) after the write commits.
        await _bus.Publish(
            new StockSecondaryCikAttached(commonStock.Id, commonStock.Ticker, normalized)
        );
        return null;
    }

    /// <summary>
    /// Removes a previously attached secondary CIK. Already-imported documents and
    /// facts stay (they were the operator's deliberate backfill); the scrapers simply
    /// stop sweeping the detached CIK. Returns null on success, otherwise a reason.
    /// </summary>
    public async Task<string> DetachSecondaryCik(CommonStock commonStock, string cik)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        var normalized = NormalizeCik(cik);
        if (normalized == null || !commonStock.SecondaryCiks.Contains(normalized))
        {
            return $"CIK {cik} is not attached to this stock.";
        }

        commonStock.SecondaryCiks = commonStock
            .SecondaryCiks.Where(c => c != normalized)
            .ToList();
        await _commonStockRepository.SaveChanges();
        return null;
    }

    // CIKs are stored with leading zeros trimmed (matching SEC filer CIKs elsewhere in
    // the model); a CIK is one to ten digits.
    internal static string NormalizeCik(string cik)
    {
        var trimmed = cik?.Trim().TrimStart('0');
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 10 || !trimmed.All(char.IsAsciiDigit))
        {
            return null;
        }
        return trimmed;
    }

    /// <summary>
    /// Stages a <see cref="CommonStockTickerAlias"/> for a primary ticker the stock is
    /// abandoning, so URLs published under the old symbol can 301 to the current one.
    /// Stages only — no SaveChanges: the caller is the SEC sync mid-rename, and the alias
    /// must commit (or roll back) atomically with the rename itself.
    /// <para>
    /// Semantics differ from the CUSIP alias on purpose. A CUSIP identifies one security
    /// forever, so the first owner keeps a retired CUSIP; tickers are recycled across
    /// unrelated issuers, so redirects are last-writer-wins:
    /// an alias equal to any LIVE primary or secondary ticker is never recorded (the live
    /// symbol would shadow it anyway — recording it would only seed a stale row for the
    /// day the live holder renames); and recording a symbol another stock retired earlier
    /// deletes that stale alias first — the most recent holder owns the redirect.
    /// </para>
    /// Returns the staged entity, or null when nothing was staged, so the caller can
    /// detach it if the surrounding update is rolled back.
    /// </summary>
    public async Task<CommonStockTickerAlias> RecordTickerAlias(
        CommonStock commonStock,
        string retiredTicker
    )
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (
            string.IsNullOrWhiteSpace(retiredTicker)
            || string.Equals(
                retiredTicker.Trim(),
                commonStock.Ticker,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return null;
        }

        var normalized = retiredTicker.Trim().ToUpperInvariant();

        // The stock keeping the symbol as a secondary listing isn't a retirement — the live
        // lookup still resolves it, so an alias would never fire (and would turn into a wrong
        // redirect the day the secondary is dropped without a rename).
        if (
            commonStock.SecondaryTickers != null
            && commonStock.SecondaryTickers.Any(t =>
                string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return null;
        }

        // Never shadow a live symbol: if any OTHER stock currently lists it (primary or
        // secondary), the live resolution wins on every lookup and the alias would only
        // linger as a wrong redirect after that holder eventually renames. The caller is the
        // sync MID-RENAME — this stock's own row still holds the retired symbol in the
        // database (the new ticker is staged in memory, unflushed, and EF never flushes
        // before a query) — so the check must exclude the stock itself or it matches its own
        // stale row and no alias is ever recorded on the one path that matters.
        var liveHolder = await _commonStockRepository
            .GetAll()
            .AnyAsync(cs =>
                cs.Id != commonStock.Id
                && (cs.Ticker == normalized || cs.SecondaryTickers.Contains(normalized))
            );
        if (liveHolder)
        {
            return null;
        }

        // Re-adoption cleanup — the deletion half of last-writer-wins the redirect design
        // depends on: the symbol this stock is renaming TO may sit in the alias map from an
        // earlier retirement (its own A→B→A round trip, or another issuer's). Once it is live
        // again the alias is at best shadowed and at worst a wrong redirect, so it goes.
        var adopted = commonStock.Ticker?.ToUpperInvariant();
        if (adopted != null)
        {
            var staleAdopted = await _commonStockRepository
                .GetTickerAliases()
                .FirstOrDefaultAsync(a => a.Ticker == adopted);
            if (staleAdopted != null)
            {
                _commonStockRepository.DeleteTickerAlias(staleAdopted);
            }
        }

        // Last-writer-wins: a symbol another stock retired earlier now belongs to this
        // stock's history — delete the stale alias so the unique index accepts the new
        // row (also covers this stock re-retiring a symbol it held twice).
        var existing = await _commonStockRepository
            .GetTickerAliases()
            .FirstOrDefaultAsync(a => a.Ticker == normalized);
        if (existing != null)
        {
            if (existing.CommonStockId == commonStock.Id)
            {
                return null;
            }
            _commonStockRepository.DeleteTickerAlias(existing);
        }

        return _commonStockRepository.AddTickerAlias(
            new CommonStockTickerAlias { CommonStockId = commonStock.Id, Ticker = normalized }
        );
    }

    /// <summary>
    /// Sets the company's fiscal year-end (month 1-12, optional day 1-31),
    /// sourced from SEC EDGAR's submissions <c>fiscalYearEnd</c> field. A
    /// no-op change persists nothing. Saves directly via the repository — like
    /// <see cref="SetCusip"/>, this mutates a single non-key field and must not
    /// re-run the full ticker/CIK uniqueness validation.
    /// </summary>
    public async Task SetFiscalYearEnd(CommonStock commonStock, int month, int? day)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        if (month is < 1 or > 12)
        {
            throw new DomainValidationException(
                $"Fiscal year-end month must be between 1 and 12, got {month}"
            );
        }

        if (day is < 1 or > 31)
        {
            throw new DomainValidationException(
                $"Fiscal year-end day must be between 1 and 31, got {day}"
            );
        }

        if (day is not null && day > DateTime.DaysInMonth(2000, month))
        {
            throw new DomainValidationException($"Day {day} is invalid for month {month}");
        }

        if (commonStock.FiscalYearEndMonth == month && commonStock.FiscalYearEndDay == day)
        {
            return;
        }

        commonStock.FiscalYearEndMonth = month;
        commonStock.FiscalYearEndDay = day;
        await _commonStockRepository.SaveChanges();
    }

    /// <summary>
    /// Sets the company's SEC classification — the submissions <c>sic</c> code and
    /// <c>entityType</c> — used to tell operating companies apart from pooled
    /// investment vehicles. Blank values are normalised to null so a missing SIC
    /// stays eligible for a later refill rather than masquerading as classified. A
    /// no-op change persists nothing. Saves directly via the repository — like
    /// <see cref="SetFiscalYearEnd"/>, this mutates non-key fields and must not
    /// re-run the full ticker/CIK uniqueness validation.
    /// </summary>
    public async Task SetSecClassification(CommonStock commonStock, string sic, string entityType)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        var normalizedSic = string.IsNullOrWhiteSpace(sic) ? null : sic.Trim();
        var normalizedEntityType = string.IsNullOrWhiteSpace(entityType) ? null : entityType.Trim();

        if (commonStock.Sic == normalizedSic && commonStock.EntityType == normalizedEntityType)
        {
            return;
        }

        commonStock.Sic = normalizedSic;
        commonStock.EntityType = normalizedEntityType;
        await _commonStockRepository.SaveChanges();
    }

    public async Task<CommonStock> Create(CommonStock commonStock)
    {
        await ValidateCommonStock(commonStock, true);
        _commonStockRepository.Add(commonStock);
        await _commonStockRepository.SaveChanges();
        return commonStock;
    }

    public async Task<CommonStock> Update(CommonStock commonStock)
    {
        await ValidateCommonStock(commonStock, false);
        await _commonStockRepository.SaveChanges();
        return commonStock;
    }

    private async Task ValidateCommonStock(CommonStock commonStock, bool isInsert)
    {
        ArgumentNullException.ThrowIfNull(commonStock);

        // Required fields: a whitespace-only value is not a provided value.
        // Ticker is the globally-unique key and the lookup key, so accepting
        // whitespace would corrupt the uniqueness invariant and ticker lookups.
        RequireNonBlank(commonStock.Ticker, "Ticker");
        RequireNonBlank(commonStock.Name, "Name");
        RequireNonBlank(commonStock.Cik, "Cik");

        if (commonStock.MarketCapitalization < 0)
        {
            throw new DomainValidationException("MarketCapitalization cannot be negative");
        }

        if (commonStock.SharesOutStanding < 0)
        {
            throw new DomainValidationException("SharesOutStanding cannot be negative");
        }

        // Primary ticker must be globally unique across all companies.
        var existingByTicker = await _commonStockRepository.GetByPrimaryTicker(commonStock.Ticker);
        if (existingByTicker != null && (isInsert || existingByTicker.Id != commonStock.Id))
        {
            throw new DomainValidationException(
                $"CommonStock with ticker {commonStock.Ticker} already exists"
            );
        }

        var existingByCik = await _commonStockRepository.GetByCik(commonStock.Cik);
        if (existingByCik != null && (isInsert || existingByCik.Id != commonStock.Id))
        {
            throw new DomainValidationException(
                $"CommonStock with cik {commonStock.Cik} already exists"
            );
        }

        // Secondary tickers are allowed to overlap with primary or secondary tickers of other
        // companies. In SEC filings a preferred-share ticker can legitimately appear under both
        // the parent REIT filer and its operating-partnership filer, so cross-company overlap
        // is valid. Lookups resolve ambiguity via GetByTicker's primary-first ordering.
    }

    private static void RequireNonBlank(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{name} is required");
        }
    }
}
