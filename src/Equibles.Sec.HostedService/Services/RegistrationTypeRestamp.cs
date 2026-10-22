using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Re-applies <see cref="ListedSecurityClassifier"/> to every stored <see cref="EquitySecurity.RegistrationTitle"/>.
/// The type is otherwise written only when a new filing is extracted, so a classifier change would
/// never reach an issuer that stopped filing; this pass makes every change reach the stored rows.
/// </summary>
public static class RegistrationTypeRestamp
{
    public static async Task<int> Apply(
        DbContext dbContext,
        CancellationToken cancellationToken = default
    )
    {
        var securities = dbContext.Set<EquitySecurity>();
        var stored = await securities
            .AsNoTracking()
            .Where(security => security.RegistrationTitle != null)
            .Select(security => new
            {
                security.Id,
                security.RegistrationTitle,
                security.RegistrationType,
            })
            .ToListAsync(cancellationToken);

        var restamped = 0;
        foreach (var row in stored)
        {
            var classified = ListedSecurityClassifier.Classify(row.RegistrationTitle);
            if (classified == row.RegistrationType)
                continue;
            // Conditioned on the values just read, so a filing extracted meanwhile keeps its own stamp.
            restamped += await securities
                .Where(security =>
                    security.Id == row.Id
                    && security.RegistrationTitle == row.RegistrationTitle
                    && security.RegistrationType == row.RegistrationType
                )
                .ExecuteUpdateAsync(
                    update => update.SetProperty(security => security.RegistrationType, classified),
                    cancellationToken
                );
        }
        return restamped;
    }
}
