using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Helpers;

public sealed class FinancialFactBatchInterceptor : DbCommandInterceptor
{
    public List<int> FactBatchSizes { get; } = [];

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (
            command.CommandText.StartsWith("INSERT INTO", StringComparison.Ordinal)
            && command.CommandText.Contains("\"FinancialFact\"", StringComparison.Ordinal)
        )
            FactBatchSizes.Add(result);

        return ValueTask.FromResult(result);
    }
}
