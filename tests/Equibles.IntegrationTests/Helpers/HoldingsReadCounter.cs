using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Helpers;

internal sealed class HoldingsReadCounter : DbCommandInterceptor
{
    public int Reads { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        if (command.CommandText.Contains("InstitutionalHolding"))
            Reads++;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
