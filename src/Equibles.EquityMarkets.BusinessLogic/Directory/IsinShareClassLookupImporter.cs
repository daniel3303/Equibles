using System.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.OpenFigi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.EquityMarkets.BusinessLogic.Directory;

/// <summary>
/// Records OpenFIGI's answer for the listed ISINs of issuers that hold no CIK: the US line of the same
/// share and whether it is quoted over the counter. A foreign share's US over-the-counter line carries
/// no ISIN in the SEC directory, so this answer is the only identifier linking the two. It is stored as
/// source evidence for duplicate-issuer review and never joins anything itself.
/// </summary>
[Service]
public partial class IsinShareClassLookupImporter(
    IServiceScopeFactory scopeFactory,
    OpenFigiClient figi,
    ILogger<IsinShareClassLookupImporter> logger
)
{
    public const int BatchSize = 100;
    public const string EvidenceSource = "openfigi-isin";

    // Two jobs an ISIN: its US composite line and its over-the-counter quote.
    private const int IsinsPerRequest = OpenFigiClient.JobsPerRequest / 2;
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(30);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromDays(1);

    /// <summary>Looks up one batch; returns how many ISINs were due so the caller can continue.</summary>
    public async Task<int> Refresh(CancellationToken cancellationToken)
    {
        List<string> due;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            due = await scope
                .ServiceProvider.GetRequiredService<IsinShareClassLookupRepository>()
                .GetDue(DateTime.UtcNow, BatchSize, cancellationToken);
        }
        var found = 0;
        foreach (var isins in due.Chunk(IsinsPerRequest))
        {
            IReadOnlyList<OpenFigiAnswer> answers;
            try
            {
                answers = await figi.Map(
                    isins
                        .SelectMany(isin =>
                            new[]
                            {
                                new OpenFigiJob("ID_ISIN", isin, IsinShareClassAnswer.UsComposite),
                                new OpenFigiJob(
                                    "ID_ISIN",
                                    isin,
                                    IsinShareClassAnswer.UsOverTheCounter
                                ),
                            }
                        )
                        .ToList(),
                    cancellationToken
                );
            }
            catch (Exception exception)
                when (!cancellationToken.IsCancellationRequested
                    && exception
                        is HttpRequestException
                            or InvalidDataException
                            or OperationCanceledException
                )
            {
                foreach (var isin in isins)
                    await Fail(isin, exception.Message, RetryInterval, cancellationToken);
                continue;
            }
            for (var index = 0; index < isins.Length; index++)
            {
                var (composite, quoted) = (answers[index * 2], answers[index * 2 + 1]);
                // OpenFIGI refused this one ISIN; its form will not change before the next refresh.
                if ((composite.Error ?? quoted.Error) is { } refusal)
                {
                    await Fail(isins[index], refusal, RefreshInterval, cancellationToken);
                    continue;
                }
                var answer = IsinShareClassAnswer.Read(
                    isins[index],
                    composite.Instruments,
                    quoted.Instruments
                );
                await Store(isins[index], answer, cancellationToken);
                if (answer.UsTickers.Count > 0)
                    found++;
            }
        }
        LogBatch(logger, due.Count, found);
        return due.Count;
    }

    private async Task Store(
        string isin,
        IsinShareClassAnswer answer,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var lookups = scope.ServiceProvider.GetRequiredService<IsinShareClassLookupRepository>();
        var records =
            scope.ServiceProvider.GetRequiredService<EquityDirectorySourceRecordRepository>();
        await using var transaction = await lookups.CreateTransaction(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var recordId = await records.Append(
            EvidenceSource,
            isin,
            answer.PayloadJson,
            cancellationToken
        );
        var now = DateTime.UtcNow;
        var row = await Row(lookups, isin, cancellationToken);
        row.ShareClassFigi = answer.ShareClassFigi;
        row.UsTickers = answer.UsTickers.ToList();
        row.UsSecurityType = answer.UsSecurityType;
        row.QuotedOverTheCounter = answer.QuotedOverTheCounter;
        row.SourceRecordId = recordId;
        row.CheckedAt = now;
        row.NextAttemptAt = now + RefreshInterval;
        row.LastError = null;
        await lookups.SaveChanges();
        await transaction.CommitAsync(cancellationToken);
    }

    // The last answer stays; only the next attempt moves.
    private async Task Fail(
        string isin,
        string error,
        TimeSpan retry,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var lookups = scope.ServiceProvider.GetRequiredService<IsinShareClassLookupRepository>();
        var row = await Row(lookups, isin, cancellationToken);
        row.NextAttemptAt = DateTime.UtcNow + retry;
        row.LastError = error?.Length > 2000 ? error[..2000] : error;
        await lookups.SaveChanges();
        LogFailed(logger, isin, error);
    }

    private static async Task<IsinShareClassLookup> Row(
        IsinShareClassLookupRepository lookups,
        string isin,
        CancellationToken cancellationToken
    )
    {
        var row = await lookups
            .GetAll()
            .SingleOrDefaultAsync(row => row.Isin == isin, cancellationToken);
        if (row != null)
            return row;
        row = new IsinShareClassLookup { Isin = isin };
        lookups.Add(row);
        return row;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "OpenFIGI share class lookup batch complete: {Checked} checked, {Found} with a US line"
    )]
    private static partial void LogBatch(ILogger logger, int @checked, int found);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "OpenFIGI share class lookup for {Isin} remains unresolved: {Reason}"
    )]
    private static partial void LogFailed(ILogger logger, string isin, string reason);
}
