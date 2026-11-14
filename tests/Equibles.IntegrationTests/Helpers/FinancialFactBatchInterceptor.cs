using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.IntegrationTests.Helpers;

public sealed class FinancialFactBatchInterceptor : DbCommandInterceptor
{
    public List<int> FactBatchSizes { get; } = [];

    /// <summary>The table each insert targeted, in execution order.</summary>
    public List<string> InsertedTables { get; } = [];

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        const string insert = "INSERT INTO \"";
        if (command.CommandText.StartsWith(insert, StringComparison.Ordinal))
        {
            var table = command.CommandText[insert.Length..];
            InsertedTables.Add(table[..table.IndexOf('"')]);
            if (command.CommandText.Contains("\"FinancialFact\"", StringComparison.Ordinal))
                FactBatchSizes.Add(result);
        }

        return ValueTask.FromResult(result);
    }
}
