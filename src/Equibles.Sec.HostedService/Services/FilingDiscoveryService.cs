using System.Collections.Concurrent;
using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Extensions;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Extensions;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

public class FilingDiscoveryService : IFilingDiscoveryService
{
    internal const string DailyIndexStateName = "SecFilingDiscovery.DailyIndex";

    // Cross-cycle memos — the service is resolved per scrape cycle, so state
    // that must survive between cycles is static (same pattern as
    // CompanySyncService's blank-website memo). The worker is single-threaded;
    // the concurrent dictionary is for safety, not contention.
    private static DateTime _lastFeedPollAtUtc;
    private static readonly ConcurrentDictionary<string, DateTime> SeenFeedEntries = new();
    private static readonly TimeSpan SeenFeedEntryRetention = TimeSpan.FromHours(12);

    // Feed-flagged filings not yet confirmed by their company's submissions
    // enumeration, keyed "accession|numericCik" — the same two-sided discipline
    // as SeenFeedEntries, because one accession appears once per associated
    // entity and each tracked side needs its own confirmation. The submissions
    // JSON trails acceptance by minutes, so an enumeration run seconds after
    // the feed flag can miss the filing; entries here keep re-dirtying the
    // company until MarkAccessionsEnumerated sees the accession under that CIK,
    // else the filing silently waits ~a day for the daily-index backstop.
    private static readonly ConcurrentDictionary<
        string,
        PendingFeedAccession
    > PendingFeedAccessions = new();

    private static string PendingKey(string accessionNumber, long numericCik) =>
        $"{accessionNumber}|{numericCik}";

    private const int FeedPageSize = 100;

    private static readonly TimeZoneInfo EasternTimeZone = ResolveEasternTimeZone();

    private readonly ISecEdgarClient _secEdgarClient;
    private readonly BackfillStateRepository _backfillStateRepository;
    private readonly DocumentScraperOptions _options;
    private readonly ILogger<FilingDiscoveryService> _logger;

    public FilingDiscoveryService(
        ISecEdgarClient secEdgarClient,
        BackfillStateRepository backfillStateRepository,
        IOptions<DocumentScraperOptions> options,
        ILogger<FilingDiscoveryService> logger
    )
    {
        _secEdgarClient = secEdgarClient;
        _backfillStateRepository = backfillStateRepository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<CommonStock>> DiscoverCompaniesWithNewFilings(
        IReadOnlyList<CommonStock> trackedCompanies,
        CancellationToken cancellationToken = default
    )
    {
        var cikToCompany = BuildCikMap(trackedCompanies);
        var syncedForms = BuildSyncedFormSet();
        var dirty = new Dictionary<Guid, CommonStock>();

        // Captured before the collectors run so entries seeded during this
        // pass's feed poll can never look retry-due in this same pass, even
        // when a slow multi-page poll makes the pass span minutes.
        var utcNow = DateTime.UtcNow;

        await CollectFromRecentFeed(cikToCompany, syncedForms, dirty, cancellationToken);
        await CollectFromDailyIndex(cikToCompany, syncedForms, dirty, cancellationToken);
        ReflagPendingAccessions(cikToCompany, dirty, utcNow);

        return [.. dirty.Values];
    }

    /// <inheritdoc />
    public bool HasPendingFeedAccessions => !PendingFeedAccessions.IsEmpty;

    /// <inheritdoc />
    public void MarkAccessionsEnumerated(
        IReadOnlyCollection<(string AccessionNumber, string Cik)> enumeratedFilings
    )
    {
        if (enumeratedFilings == null || enumeratedFilings.Count == 0)
            return;
        if (PendingFeedAccessions.IsEmpty)
            return;

        foreach (var (accessionNumber, cik) in enumeratedFilings)
        {
            if (string.IsNullOrEmpty(accessionNumber) || !long.TryParse(cik, out var numericCik))
                continue;

            if (
                !PendingFeedAccessions.TryRemove(
                    PendingKey(accessionNumber, numericCik),
                    out var pending
                )
            )
                continue;

            if (pending.RetryLogged)
            {
                _logger.LogInformation(
                    "Feed-flagged filing {AccessionNumber} appeared in CIK {Cik}'s submissions enumeration {Minutes:F1} minutes after its feed flag",
                    accessionNumber,
                    numericCik,
                    (DateTime.UtcNow - pending.FirstSeenAtUtc).TotalMinutes
                );
            }
        }
    }

    /// <summary>
    /// Keeps companies with unconfirmed feed-flagged filings dirty so they are
    /// re-enumerated until the submissions JSON catches up. Without this, a
    /// company enumerated inside the JSON's lag window is stamped as synced,
    /// its feed entry stays "seen" for 12h, and the filing silently waits for
    /// the next-morning daily index — exactly the after-the-bell earnings 8-Ks
    /// the realtime layer exists for. Retries are rate-limited per accession
    /// and give up after the configured expiry (the daily index still owns
    /// recovery past that).
    /// </summary>
    private void ReflagPendingAccessions(
        Dictionary<long, CommonStock> cikToCompany,
        Dictionary<Guid, CommonStock> dirty,
        DateTime utcNow
    )
    {
        if (PendingFeedAccessions.IsEmpty)
            return;

        var due = new List<(string Key, PendingFeedAccession Pending, CommonStock Company)>();

        foreach (var (key, pending) in PendingFeedAccessions)
        {
            var expired =
                utcNow - pending.FirstSeenAtUtc
                > TimeSpan.FromMinutes(_options.FeedPendingExpiryMinutes);
            if (expired || pending.RetryCount >= _options.FeedPendingMaxRetries)
            {
                PendingFeedAccessions.TryRemove(key, out _);
                _logger.LogWarning(
                    "Feed-flagged filing {PendingKey} never appeared in the submissions enumeration ({Reason}: {Retries} retries over {Minutes:F0} minutes) — abandoning the realtime retry; the daily index will backstop it",
                    key,
                    expired ? "expired" : "retries exhausted",
                    pending.RetryCount,
                    (utcNow - pending.FirstSeenAtUtc).TotalMinutes
                );
                continue;
            }

            // The filer left the tracked universe mid-retry (delisted or
            // dropped by company sync) — nothing to re-enumerate.
            if (!cikToCompany.TryGetValue(pending.Cik, out var company))
            {
                PendingFeedAccessions.TryRemove(key, out _);
                continue;
            }

            if (
                utcNow - pending.LastRetriedAtUtc
                < TimeSpan.FromSeconds(_options.FeedPendingRetrySeconds)
            )
                continue;

            due.Add((key, pending, company));
        }

        // Oldest first under a per-cycle cap: a submissions-JSON stall must not
        // turn every recently flagged filer into one simultaneous
        // re-enumeration storm against an already-degraded EDGAR.
        foreach (
            var (key, pending, company) in due.OrderBy(d => d.Pending.FirstSeenAtUtc)
                .Take(_options.MaxPendingReflagsPerCycle)
        )
        {
            pending.LastRetriedAtUtc = utcNow;
            pending.RetryCount++;
            dirty[company.Id] = company;

            // Expected-path event (the JSON routinely lags acceptance), so
            // Information — the warnings-only file sink stays reserved for the
            // abandonment above.
            if (!pending.RetryLogged)
            {
                pending.RetryLogged = true;
                _logger.LogInformation(
                    "Filing {PendingKey} for {Ticker} was feed-flagged {Minutes:F0} minutes ago but is not yet enumerable in the submissions JSON — keeping the company flagged for re-enumeration",
                    key,
                    company.Ticker,
                    (utcNow - pending.FirstSeenAtUtc).TotalMinutes
                );
            }
            else
            {
                _logger.LogDebug(
                    "Re-flagging {Ticker} for pending feed accession {PendingKey}",
                    company.Ticker,
                    key
                );
            }
        }
    }

    /// <summary>
    /// Polls the "Latest Filings" ATOM feed when the poll interval has elapsed
    /// and marks tracked filers dirty. Pages until it overlaps entries already
    /// seen by a previous poll (or the page cap), so a burst bigger than one
    /// page is still swept. A failed poll is only logged: the daily index and
    /// the reconciliation sweep guarantee eventual pickup.
    /// </summary>
    private async Task CollectFromRecentFeed(
        Dictionary<long, CommonStock> cikToCompany,
        HashSet<string> syncedForms,
        Dictionary<Guid, CommonStock> dirty,
        CancellationToken cancellationToken
    )
    {
        var utcNow = DateTime.UtcNow;
        if (utcNow - _lastFeedPollAtUtc < TimeSpan.FromSeconds(_options.RecentFeedPollSeconds))
            return;
        _lastFeedPollAtUtc = utcNow;

        PruneSeenFeedEntries(utcNow);

        for (var page = 0; page < _options.RecentFeedMaxPages; page++)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            List<EdgarRecentFilingEntry> entries;
            try
            {
                entries = await _secEdgarClient.GetRecentFilings(
                    page * FeedPageSize,
                    FeedPageSize,
                    cancellationToken
                );
            }
            // Best-effort layer: any fetch/parse failure (HTTP error, timeout —
            // which surfaces as TaskCanceledException, not HttpRequestException —
            // transport IO, malformed XML) must not abort the cycle and discard
            // the dirty set; only a genuine shutdown propagates.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Latest-filings feed poll failed on page {Page}; the daily index and reconciliation sweep will backstop",
                    page
                );
                return;
            }

            var sawKnownEntry = false;
            foreach (var entry in entries)
            {
                // The same accession appears once per associated entity (filer,
                // subject, reporting owner), each with its own CIK — key on both
                // so a page boundary can't hide the tracked side of a filing.
                var seenKey = $"{entry.AccessionNumber}|{entry.Cik}";
                if (!SeenFeedEntries.TryAdd(seenKey, utcNow))
                {
                    sawKnownEntry = true;
                    continue;
                }

                if (!syncedForms.Contains(entry.FormType))
                    continue;

                if (
                    !long.TryParse(entry.Cik, out var numericCik)
                    || !cikToCompany.TryGetValue(numericCik, out var company)
                )
                    continue;

                dirty[company.Id] = company;

                // Track the accession until the company's enumeration confirms
                // it under this CIK — the submissions JSON may not list it yet
                // (see ReflagPendingAccessions). LastRetriedAtUtc starts now:
                // the company is already dirty this cycle, so the first re-flag
                // waits a full retry interval.
                if (!string.IsNullOrEmpty(entry.AccessionNumber))
                {
                    PendingFeedAccessions.TryAdd(
                        PendingKey(entry.AccessionNumber, numericCik),
                        new PendingFeedAccession
                        {
                            Cik = numericCik,
                            FirstSeenAtUtc = utcNow,
                            LastRetriedAtUtc = utcNow,
                        }
                    );
                }
            }

            // Overlap with the previous poll (or a short page) means the window
            // between polls is fully covered — stop paging.
            if (sawKnownEntry || entries.Count < FeedPageSize)
                return;
        }

        _logger.LogWarning(
            "Latest-filings feed poll exhausted {Pages} pages without overlapping the previous poll — a burst may have scrolled entries out; the daily index will catch them",
            _options.RecentFeedMaxPages
        );
    }

    /// <summary>
    /// Walks the immutable per-day master indexes from the persisted watermark
    /// through the latest final day, marking tracked filers dirty. The
    /// watermark only advances past a day whose index was fetched and parsed,
    /// so throttles and outages hold it back and the gap is re-swept next
    /// cycle — never silently skipped. Cold start pins the watermark to the
    /// latest final day instead of replaying history: the reconciliation sweep
    /// owns historical coverage.
    /// </summary>
    private async Task CollectFromDailyIndex(
        Dictionary<long, CommonStock> cikToCompany,
        HashSet<string> syncedForms,
        Dictionary<Guid, CommonStock> dirty,
        CancellationToken cancellationToken
    )
    {
        var latestFinalDay = LatestFinalIndexDay(DateTime.UtcNow);
        var state = await _backfillStateRepository.GetByName(DailyIndexStateName);

        if (state?.Floor == null)
        {
            if (state == null)
            {
                state = new BackfillState { Name = DailyIndexStateName };
                _backfillStateRepository.Add(state);
            }
            state.Floor = ToUtcDate(latestFinalDay);
            await _backfillStateRepository.SaveChanges();
            _logger.LogInformation(
                "Daily-index discovery watermark initialized at {Day:yyyy-MM-dd}",
                latestFinalDay
            );
            return;
        }

        var floor = DateOnly.FromDateTime(state.Floor.Value);
        var formPrefixes = syncedForms.ToList();
        var processedDays = 0;

        for (
            var day = floor.AddDays(1);
            day <= latestFinalDay && processedDays < _options.DailyIndexMaxDaysPerCycle;
            day = day.AddDays(1)
        )
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            List<EdgarDailyIndexEntry> entries;
            try
            {
                entries = await _secEdgarClient.GetDailyIndexForForms(
                    day,
                    formPrefixes,
                    cancellationToken
                );
            }
            // NEVER advance past a day that failed to fetch — that would
            // silently drop its filings until the reconciliation sweep. Broad
            // by design (timeouts surface as TaskCanceledException, transport
            // failures as IOException) with a guard so a genuine shutdown still
            // propagates; the watermark held below makes the failure safe.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Daily index fetch failed for {Day:yyyy-MM-dd}; holding discovery watermark at {Floor:yyyy-MM-dd}",
                    day,
                    floor
                );
                break;
            }

            foreach (var entry in entries)
            {
                // The client filter is prefix-based (so "4" also returns 424B2
                // rows); re-check with an exact match before dirtying a company.
                if (!syncedForms.Contains(entry.FormType))
                    continue;

                if (TryResolveCompany(cikToCompany, entry.Cik, out var company))
                    dirty[company.Id] = company;
            }

            state.Floor = ToUtcDate(day);
            floor = day;
            processedDays++;
        }

        if (processedDays > 0)
        {
            await _backfillStateRepository.SaveChanges();
            _logger.LogInformation(
                "Daily-index discovery advanced watermark to {Floor:yyyy-MM-dd} ({Days} day(s) processed, {Dirty} companies flagged so far)",
                floor,
                processedDays,
                dirty.Count
            );
        }
    }

    /// <summary>
    /// The most recent day whose master index is final. The index for day D
    /// keeps growing until EDGAR's evening batch completes, so D only becomes
    /// eligible from 06:00 Eastern on D+1 — processing it earlier would advance
    /// the watermark past filings that hadn't been written to the index yet.
    /// </summary>
    internal static DateOnly LatestFinalIndexDay(DateTime utcNow)
    {
        var easternNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, EasternTimeZone);
        var previousDay = DateOnly.FromDateTime(easternNow).AddDays(-1);
        return easternNow.Hour >= 6 ? previousDay : previousDay.AddDays(-1);
    }

    /// <summary>
    /// Maps every tracked CIK — primary and subsidiary — to its company,
    /// keyed numerically because EDGAR surfaces CIKs both zero-padded
    /// ("0000320193") and bare ("320193").
    /// </summary>
    internal static Dictionary<long, CommonStock> BuildCikMap(
        IReadOnlyList<CommonStock> trackedCompanies
    )
    {
        var map = new Dictionary<long, CommonStock>();
        foreach (var company in trackedCompanies)
        {
            if (long.TryParse(company.Cik, out var primaryCik))
                map.TryAdd(primaryCik, company);

            foreach (var secondaryCik in company.SecondaryCiks ?? [])
            {
                if (long.TryParse(secondaryCik, out var cik))
                    map.TryAdd(cik, company);
            }
        }

        return map;
    }

    private HashSet<string> BuildSyncedFormSet()
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var documentType in _options.DocumentTypesToSync)
        {
            var filter = documentType.ToSecEdgarFilter();
            if (filter != null)
                forms.Add(filter.Value.GetFormName());
        }

        return forms;
    }

    private static bool TryResolveCompany(
        Dictionary<long, CommonStock> cikToCompany,
        string cik,
        out CommonStock company
    )
    {
        company = null;
        return long.TryParse(cik, out var numericCik)
            && cikToCompany.TryGetValue(numericCik, out company);
    }

    // BackfillState.Floor is a timestamptz; Npgsql rejects non-UTC kinds.
    private static DateTime ToUtcDate(DateOnly day) =>
        day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static void PruneSeenFeedEntries(DateTime utcNow)
    {
        foreach (var entry in SeenFeedEntries)
        {
            if (utcNow - entry.Value > SeenFeedEntryRetention)
                SeenFeedEntries.TryRemove(entry.Key, out _);
        }
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    // Test seam: the memos are static (they must outlive the per-cycle service),
    // so suites reset them to stay order-independent.
    internal static void ResetCrossCycleStateForTests()
    {
        _lastFeedPollAtUtc = default;
        SeenFeedEntries.Clear();
        PendingFeedAccessions.Clear();
    }
}
