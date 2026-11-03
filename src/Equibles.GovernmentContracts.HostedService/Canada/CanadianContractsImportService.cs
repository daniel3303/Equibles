using Equibles.Core.AutoWiring;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Services;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.GovernmentContracts.HostedService.Canada;

/// <summary>
/// Imports Government of Canada contracts over $10,000 whose vendor resolves, by exact
/// normalised name, to an issuer listed on a Canadian exchange. One row per contract holds its
/// latest reported value: an amendment restates the contract's amended total under the same
/// procurement id, so summing reports would count a contract once per amendment.
/// </summary>
[Service]
public class CanadianContractsImportService : IImporter
{
    public const string ClientName = "OpenCanadaContracts";
    public const string CountryCode = "CA";

    internal const string ScanStateName = "canada-contract-periods";

    // Raise when matching changes: the scan restarts at the first quarter so newly matched contracts are found.
    internal const int MatchingVersion = 1;

    // Departments publish a quarter about a month after it ends and amend it later, so the
    // current quarter and the three before it are read again every cycle.
    internal const int RecentPeriods = 4;

    // About twenty pages a quarter; the backlog drains a few quarters per cycle and the worker continues straight away.
    internal const int MaxBacklogPeriodsPerCycle = 8;

    private static readonly TimeSpan PagePause = TimeSpan.FromMilliseconds(250);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _clients;
    private readonly RecipientResolver _resolver;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CanadianContractsImportService> _logger;

    public CanadianContractsImportService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory clients,
        RecipientResolver resolver,
        TimeProvider timeProvider,
        ILogger<CanadianContractsImportService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _clients = clients;
        _resolver = resolver;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>True when quarters older than the recent window remain unread after this cycle.</summary>
    public bool BacklogRemains { get; private set; }

    public async Task Import(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var current = CanadianReportingPeriod.Containing(today);
        var next = await NextUnreadPeriod(cancellationToken);

        var recentStart = current;
        for (var count = 1; count < RecentPeriods; count++)
            recentStart = recentStart.Previous;
        var periods = new SortedSet<CanadianReportingPeriod>();
        for (
            var period = next;
            period <= current && periods.Count < MaxBacklogPeriodsPerCycle;
            period = period.Next
        )
            periods.Add(period);
        for (var period = recentStart; period <= current; period = period.Next)
            periods.Add(period);

        var lookup = await _resolver.BuildCanadianLookup(cancellationToken);
        if (lookup.Count == 0)
        {
            _logger.LogWarning(
                "Canadian contracts import skipped: no issuer is listed on a Canadian exchange yet"
            );
            return;
        }

        var client = _clients.CreateClient(ClientName);
        // Only the unbroken run of quarters from the cursor is marked read: re-reading the recent
        // quarters must never carry the cursor past backlog quarters this cycle did not reach.
        var frontier = next;
        foreach (var period in periods)
        {
            var rows = await ReadPeriod(client, period, cancellationToken);
            var (matched, inserted, updated) = await Store(
                rows,
                lookup,
                period,
                today,
                cancellationToken
            );
            _logger.LogInformation(
                "Canadian contracts {Period}: {Rows} reports read, {Matched} from listed vendors, {Inserted} contracts added, {Updated} updated",
                period.Code,
                rows.Count,
                matched,
                inserted,
                updated
            );
            if (period == frontier)
            {
                await MarkRead(period, cancellationToken);
                frontier = period.Next;
            }
        }

        BacklogRemains = frontier < recentStart;
    }

    private async Task<IReadOnlyList<CanadianContractRow>> ReadPeriod(
        HttpClient client,
        CanadianReportingPeriod period,
        CancellationToken cancellationToken
    )
    {
        var rows = new List<CanadianContractRow>();
        for (var offset = 0; ; offset += OpenCanadaContracts.PageSize)
        {
            using var response = await client.GetAsync(
                OpenCanadaContracts.PageAddress(period, offset),
                cancellationToken
            );
            response.EnsureSuccessStatusCode();
            var page = OpenCanadaContracts.ParsePage(
                await response.Content.ReadAsStringAsync(cancellationToken)
            );
            rows.AddRange(page);
            // A short page is the last one; the reported total can be an estimate, so it is never the stop signal.
            if (page.Count < OpenCanadaContracts.PageSize)
                return rows;
            await Task.Delay(PagePause, _timeProvider, cancellationToken);
        }
    }

    private async Task<(int Matched, int Inserted, int Updated)> Store(
        IReadOnlyList<CanadianContractRow> rows,
        IReadOnlyDictionary<string, Guid> lookup,
        CanadianReportingPeriod period,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        var resolved = rows.SelectMany(row =>
                RecipientResolver.Resolve(row.VendorName, lookup) is { } issuerId
                && row.ContractValue != null
                && !string.IsNullOrWhiteSpace(row.Department)
                && row.AwardUniqueKey.Length <= 128
                    ? [(Row: row, IssuerId: issuerId)]
                    : Array.Empty<(CanadianContractRow Row, Guid IssuerId)>()
            )
            .ToList();
        // Inside one quarter a department numbers its reports in order, so the highest number is the latest amendment.
        var latest = resolved
            .GroupBy(item => item.Row.AwardUniqueKey, StringComparer.Ordinal)
            .Select(group =>
                (
                    Latest: group.MaxBy(item => item.Row.ReferenceSequence),
                    Signed: group
                        .Select(item => OpenCanadaContracts.AwardDate(item.Row.ContractDate, today))
                        .Min()
                )
            )
            .ToList();
        if (latest.Count == 0)
            return (0, 0, 0);

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<GovernmentContractRepository>();
        var keys = latest.Select(item => item.Latest.Row.AwardUniqueKey).ToList();
        var stored = await repository
            .GetByCountry(CountryCode)
            .Where(contract => keys.Contains(contract.AwardUniqueKey))
            .ToDictionaryAsync(
                contract => contract.AwardUniqueKey,
                StringComparer.Ordinal,
                cancellationToken
            );

        var (inserted, updated) = (0, 0);
        foreach (var ((row, issuerId), signed) in latest)
        {
            var type = AwardType(row.InstrumentType);
            if (!stored.TryGetValue(row.AwardUniqueKey, out var contract))
            {
                contract = new GovernmentContract
                {
                    AwardUniqueKey = row.AwardUniqueKey,
                    CountryCode = CountryCode,
                    AwardType = type ?? GovernmentContractAwardType.Unknown,
                };
                Apply(contract, row, issuerId, period, signed);
                repository.Add(contract);
                inserted++;
                continue;
            }

            // A quarter read again after a later one supplied the contract keeps the later
            // report's value; only its instrument type can still fill a gap.
            if (contract.LastModifiedDate is { } reportedThrough && reportedThrough > period.End)
            {
                if (contract.AwardType == GovernmentContractAwardType.Unknown && type != null)
                    contract.AwardType = type.Value;
                contract.ActionDate = Earlier(contract.ActionDate, signed);
                continue;
            }
            if (type != null)
                contract.AwardType = type.Value;
            Apply(contract, row, issuerId, period, signed);
            updated++;
        }
        await repository.SaveChanges();
        return (resolved.Count, inserted, updated);
    }

    // LastModifiedDate holds the end of the quarter whose report supplied the values, which orders later reports over earlier ones.
    private static void Apply(
        GovernmentContract contract,
        CanadianContractRow row,
        Guid issuerId,
        CanadianReportingPeriod period,
        DateOnly? signed
    )
    {
        contract.EquityIssuerId = issuerId;
        contract.RecipientName = Clip(row.VendorName, 512);
        contract.AwardId = Clip(row.ProcurementId ?? row.ReferenceNumber, 128);
        contract.AwardingAgency = Clip(
            OpenCanadaContracts.EnglishTitle(row.DepartmentTitle) ?? row.Department,
            256
        );
        contract.Amount = row.ContractValue!.Value;
        contract.ActionDate = Earlier(contract.ActionDate, signed);
        contract.EndDate = OpenCanadaContracts.EndDate(row.DeliveryDate) ?? contract.EndDate;
        contract.LastModifiedDate = period.End;
        contract.Description = string.IsNullOrWhiteSpace(row.Description)
            ? contract.Description
            : row.Description;
    }

    // An amendment (A) restates a contract and says nothing about how it was let.
    private static GovernmentContractAwardType? AwardType(string instrumentType) =>
        instrumentType?.Trim().ToUpperInvariant() switch
        {
            "C" => GovernmentContractAwardType.Contract,
            "SOSA" => GovernmentContractAwardType.StandingOfferCallUp,
            _ => null,
        };

    private async Task<CanadianReportingPeriod> NextUnreadPeriod(
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var states =
            scope.ServiceProvider.GetRequiredService<GovernmentContractsScanStateRepository>();
        var state = await states.GetByName(ScanStateName);
        return
            state is { LastCompletedWindowEnd: { } end } && state.MatchingVersion >= MatchingVersion
            ? CanadianReportingPeriod.Containing(end).Next
            : CanadianReportingPeriod.First;
    }

    private async Task MarkRead(CanadianReportingPeriod period, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var states =
            scope.ServiceProvider.GetRequiredService<GovernmentContractsScanStateRepository>();
        var state = await states.GetByName(ScanStateName);
        if (state == null)
        {
            state = new GovernmentContractsScanState { Name = ScanStateName };
            states.Add(state);
        }
        if (state.MatchingVersion < MatchingVersion)
            state.LastCompletedWindowEnd = null;
        if (state.LastCompletedWindowEnd == null || state.LastCompletedWindowEnd < period.End)
            state.LastCompletedWindowEnd = period.End;
        state.MatchingVersion = MatchingVersion;
        state.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await states.SaveChanges();
    }

    // Some departments date an amendment by its own signing, so the earliest date any report states is the award date.
    private static DateOnly? Earlier(DateOnly? stored, DateOnly? reported) =>
        reported != null && (stored == null || reported < stored) ? reported : stored;

    private static string Clip(string value, int length) =>
        value == null || value.Length <= length ? value : value[..length];
}
