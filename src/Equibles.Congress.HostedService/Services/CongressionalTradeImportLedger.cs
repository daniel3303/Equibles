using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.Core.AutoWiring;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Durable year partitions for the congressional trade archive backfill.
/// </summary>
[Service]
public class CongressionalTradeImportLedger
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CongressionalTradeImportLedger(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public virtual async Task<int?> GetNextYear(
        CongressionalFilingKind kind,
        int parserVersion,
        int earliestYear,
        int latestYear,
        CancellationToken ct
    )
    {
        if (latestYear < earliestYear)
            return null;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository =
            scope.ServiceProvider.GetRequiredService<CongressionalTradeImportPartitionRepository>();
        var completedYears = await repository
            .GetByKind(kind)
            .Where(p =>
                p.ParserVersion >= parserVersion && p.Year >= earliestYear && p.Year <= latestYear
            )
            .Select(p => p.Year)
            .ToListAsync(ct);

        return SelectNextYear(completedYears, earliestYear, latestYear);
    }

    internal static int? SelectNextYear(
        IReadOnlyCollection<int> completedYears,
        int earliestYear,
        int latestYear
    )
    {
        var completed = completedYears.ToHashSet();
        for (var year = latestYear; year >= earliestYear; year--)
        {
            if (!completed.Contains(year))
                return year;
        }

        return null;
    }

    public virtual async Task RecordCompleted(
        CongressionalFilingKind kind,
        int year,
        int parserVersion,
        int filingCount,
        int transactionCount,
        CancellationToken ct
    )
    {
        var partition = new CongressionalTradeImportPartition
        {
            Kind = kind,
            Year = year,
            ParserVersion = parserVersion,
            FilingCount = filingCount,
            TransactionCount = transactionCount,
        };

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        await dbContext
            .Set<CongressionalTradeImportPartition>()
            .Upsert(partition)
            .On(p => new { p.Kind, p.Year })
            .WhenMatched(
                (_, incoming) =>
                    new CongressionalTradeImportPartition
                    {
                        FilingCount = incoming.FilingCount,
                        TransactionCount = incoming.TransactionCount,
                        ParserVersion = incoming.ParserVersion,
                        CompletionTime = incoming.CompletionTime,
                    }
            )
            .RunAsync(ct);
    }
}
