using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.GovernmentContracts.Data.Models;
using Equibles.GovernmentContracts.HostedService.Configuration;
using Equibles.GovernmentContracts.Repositories;
using Equibles.Integrations.GovernmentContracts.Contracts;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.GovernmentContracts.HostedService.Services;

[Service]
public class GovernmentContractsImportService : IImporter
{
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GovernmentContractsImportService> _logger;
    private readonly IUsaSpendingClient _client;
    private readonly RecipientResolver _resolver;
    private readonly GovernmentContractsScraperOptions _options;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;

    public GovernmentContractsImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<GovernmentContractsImportService> logger,
        IUsaSpendingClient client,
        RecipientResolver resolver,
        IOptions<GovernmentContractsScraperOptions> options,
        IOptions<WorkerOptions> workerOptions,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _client = client;
        _resolver = resolver;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var recipientLookup = await _resolver.BuildLookup(cancellationToken);
        if (recipientLookup.Count == 0)
        {
            _logger.LogWarning(
                "Government contracts import: the CommonStock universe is empty; skipping cycle"
            );
            return;
        }

        var startDate = await DetermineStartDate(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (startDate > today)
        {
            _logger.LogInformation("Government contracts import: already up to date");
            return;
        }

        _logger.LogInformation(
            "Government contracts import: scanning {Start}..{Today} (>= ${Min:N0}) against {Count} companies",
            startDate,
            today,
            _options.MinimumAwardAmount,
            recipientLookup.Count
        );

        var windowDays = Math.Max(1, _options.WindowDays);
        var totalInserted = 0;

        for (
            var windowStart = startDate;
            windowStart <= today;
            windowStart = windowStart.AddDays(windowDays)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var windowEnd = windowStart.AddDays(windowDays - 1);
            if (windowEnd > today)
                windowEnd = today;

            try
            {
                totalInserted += await ImportWindow(
                    windowStart,
                    windowEnd,
                    recipientLookup,
                    cancellationToken
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Government contracts import failed for window {Start}..{End}",
                    windowStart,
                    windowEnd
                );
                await _errorReporter.Report(
                    ErrorSource.GovernmentContractsScraper,
                    "GovernmentContractsImport.ImportWindow",
                    ex.Message,
                    ex.StackTrace,
                    $"window: {windowStart}..{windowEnd}"
                );
            }
        }

        _logger.LogInformation(
            "Government contracts import complete: {Count} new awards persisted",
            totalInserted
        );
    }

    private async Task<int> ImportWindow(
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlyDictionary<string, Guid> recipientLookup,
        CancellationToken cancellationToken
    )
    {
        var awards = await _client.GetContractAwards(
            windowStart,
            windowEnd,
            _options.MinimumAwardAmount
        );
        if (awards.Count == 0)
            return 0;

        // Resolve to public companies, map, and de-duplicate within the batch by unique key.
        var mapped = new Dictionary<string, GovernmentContract>(StringComparer.Ordinal);
        foreach (var award in awards)
        {
            var commonStockId = RecipientResolver.Resolve(award.RecipientName, recipientLookup);
            if (commonStockId == null)
                continue;

            var entity = UsaSpendingAwardMapper.Map(award, commonStockId.Value);
            if (entity != null)
                mapped[entity.AwardUniqueKey] = entity;
        }

        if (mapped.Count == 0)
            return 0;

        var existingKeys = await LoadExistingKeys(mapped.Keys, cancellationToken);
        var toInsert = mapped.Values.Where(c => !existingKeys.Contains(c.AwardUniqueKey)).ToList();
        if (toInsert.Count == 0)
            return 0;

        var inserted = await BatchPersister.Persist<
            GovernmentContract,
            GovernmentContractRepository
        >(toInsert, InsertBatchSize, _scopeFactory);

        _logger.LogDebug(
            "Government contracts {Start}..{End}: {Inserted} new of {Matched} matched ({Fetched} fetched)",
            windowStart,
            windowEnd,
            inserted,
            mapped.Count,
            awards.Count
        );
        return inserted;
    }

    private async Task<HashSet<string>> LoadExistingKeys(
        IEnumerable<string> keys,
        CancellationToken cancellationToken
    )
    {
        var keyList = keys.ToList();
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<GovernmentContractRepository>();
        return (
            await repository
                .GetAll()
                .Where(c => keyList.Contains(c.AwardUniqueKey))
                .Select(c => c.AwardUniqueKey)
                .ToListAsync(cancellationToken)
        ).ToHashSet(StringComparer.Ordinal);
    }

    private async Task<DateOnly> DetermineStartDate(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<GovernmentContractRepository>();
        var latest = await repository
            .GetAll()
            .Where(c => c.ActionDate != null)
            .MaxAsync(c => c.ActionDate, cancellationToken);

        return SyncDateResolver.Resolve(latest ?? default, _workerOptions);
    }
}
