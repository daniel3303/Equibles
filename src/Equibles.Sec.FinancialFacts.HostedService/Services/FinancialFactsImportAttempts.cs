using Equibles.Data;
using Equibles.Sec.FinancialFacts.Data.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

internal sealed class FinancialFactsImportAttempts(IServiceScopeFactory scopes, TimeProvider clock)
{
    internal bool IsDeferred(FinancialFactsSyncStatus status) =>
        status?.NextAttemptAt > clock.GetUtcNow().UtcDateTime;

    internal async Task<Guid?> TryBegin(Guid issuerId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var previous = await db.Set<FinancialFactsSyncStatus>()
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.EquityIssuerId == issuerId, cancellationToken);
        if (previous?.NextAttemptAt > now)
            return null;

        // One hour, doubling to a day. Reserve before doing work so abrupt termination
        // retains the cooldown; only the owner of the current attempt can acknowledge it.
        var attempts = Math.Min((previous?.ImportAttempts ?? 0) + 1, 6);
        var id = Guid.NewGuid();
        var status = new FinancialFactsSyncStatus
        {
            EquityIssuerId = issuerId,
            ImportAttempts = attempts,
            ImportAttemptId = id,
            NextAttemptAt = now.AddHours(Math.Min(24, Math.Pow(2, attempts - 1))),
        };
        var claimed = await db.Set<FinancialFactsSyncStatus>()
            .UpsertRange(status)
            .On(row => row.EquityIssuerId)
            .WhenMatched(
                (existing, incoming) =>
                    new FinancialFactsSyncStatus
                    {
                        ImportAttempts = incoming.ImportAttempts,
                        ImportAttemptId = incoming.ImportAttemptId,
                        NextAttemptAt = incoming.NextAttemptAt,
                    }
            )
            .UpdateIf(row => row.NextAttemptAt == null || row.NextAttemptAt <= now)
            .RunAsync(cancellationToken);
        return claimed == 1 ? id : null;
    }

    internal async Task Complete(
        Guid issuerId,
        Guid attemptId,
        DateOnly? lastFiled,
        string fingerprint,
        CancellationToken cancellationToken
    )
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Set<FinancialFactsSyncStatus>()
            .Where(row => row.EquityIssuerId == issuerId && row.ImportAttemptId == attemptId)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(row => row.LastCheckedAt, now)
                        .SetProperty(
                            row => row.LastFiledDateSeen,
                            row => lastFiled ?? row.LastFiledDateSeen
                        )
                        .SetProperty(
                            row => row.ImporterVersion,
                            FinancialFactsImportService.CurrentImporterVersion
                        )
                        .SetProperty(row => row.CalendarEvidenceFingerprint, fingerprint)
                        .SetProperty(row => row.ImportAttempts, 0)
                        .SetProperty(row => row.NextAttemptAt, (DateTime?)null)
                        .SetProperty(row => row.ImportAttemptId, (Guid?)null),
                cancellationToken
            );
    }
}
