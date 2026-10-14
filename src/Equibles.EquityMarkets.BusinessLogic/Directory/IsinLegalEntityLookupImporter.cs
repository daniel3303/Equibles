using System.Data;
using System.Text.Json;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Gleif;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

/// <summary>
/// Records GLEIF's answer for listed ISINs whose issuer has no LEI. The answer is stored as source
/// evidence for duplicate-issuer review and never assigns an LEI, which would be an inferred merge.
/// </summary>
[Service]
public partial class IsinLegalEntityLookupImporter(
    IServiceScopeFactory scopeFactory,
    GleifIdentityClient gleifClient,
    ILogger<IsinLegalEntityLookupImporter> logger
)
{
    public const int BatchSize = 100;
    public const string EvidenceSource = "gleif-isin";
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(30);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromDays(1);

    /// <summary>Looks up one batch; returns how many ISINs were due so the caller can continue.</summary>
    public async Task<int> Refresh(CancellationToken cancellationToken)
    {
        List<string> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            due = await scope
                .ServiceProvider.GetRequiredService<IsinLegalEntityLookupRepository>()
                .GetDue(DateTime.UtcNow, BatchSize, cancellationToken);
        }
        var found = 0;
        foreach (var isin in due)
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                var identity = await gleifClient.GetIssuerForIsin(isin, attempt.Token);
                await Store(isin, identity, cancellationToken);
                if (identity.LegalEntityIdentifier != null)
                    found++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is ArgumentException or InvalidDataException or JsonException)
            {
                // GLEIF answered, but not with exactly one confirmed issuer; ask again at the next refresh.
                await Fail(isin, exception.Message, RefreshInterval, cancellationToken);
            }
            catch (Exception exception)
                when (exception is HttpRequestException or OperationCanceledException)
            {
                await Fail(isin, exception.Message, RetryInterval, cancellationToken);
            }
        }
        LogBatch(logger, due.Count, found);
        return due.Count;
    }

    private async Task Store(
        string isin,
        Integrations.Gleif.Models.GleifIssuerIdentity identity,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var lookups = scope.ServiceProvider.GetRequiredService<IsinLegalEntityLookupRepository>();
        var records =
            scope.ServiceProvider.GetRequiredService<EquityDirectorySourceRecordRepository>();
        await using var transaction = await lookups.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var recordId = await records.Append(
            EvidenceSource,
            isin,
            JsonSerializer.Serialize(identity),
            cancellationToken
        );
        var now = DateTime.UtcNow;
        var row = await Row(lookups, isin, cancellationToken);
        row.LegalEntityIdentifier = identity.LegalEntityIdentifier;
        row.RelatedIsins = identity.RelatedIsins.Distinct(StringComparer.Ordinal).ToList();
        row.RelatedIsinCount = identity.RelatedIsinCount;
        row.SourceRecordId = recordId;
        row.CheckedAt = now;
        row.NextAttemptAt = now + RefreshInterval;
        row.LastError = null;
        await lookups.SaveChanges();
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task Fail(
        string isin,
        string error,
        TimeSpan retry,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var lookups = scope.ServiceProvider.GetRequiredService<IsinLegalEntityLookupRepository>();
        var row = await Row(lookups, isin, cancellationToken);
        row.NextAttemptAt = DateTime.UtcNow + retry;
        row.LastError = error?.Length > 2000 ? error[..2000] : error;
        await lookups.SaveChanges();
        LogFailed(logger, isin, error);
    }

    private static async Task<IsinLegalEntityLookup> Row(
        IsinLegalEntityLookupRepository lookups,
        string isin,
        CancellationToken cancellationToken
    )
    {
        var row = await lookups.GetAll().SingleOrDefaultAsync(row => row.Isin == isin, cancellationToken);
        if (row != null)
            return row;
        row = new IsinLegalEntityLookup { Isin = isin };
        lookups.Add(row);
        return row;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "GLEIF ISIN lookup batch complete: {Checked} checked, {Found} with an LEI"
    )]
    private static partial void LogBatch(ILogger logger, int @checked, int found);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "GLEIF ISIN lookup for {Isin} remains unresolved: {Reason}"
    )]
    private static partial void LogFailed(ILogger logger, string isin, string reason);
}
