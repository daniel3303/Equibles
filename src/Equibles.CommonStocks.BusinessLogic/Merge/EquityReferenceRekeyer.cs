using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.CommonStocks.BusinessLogic.Merge;

/// <summary>
/// Moves every row that holds one key onto another key inside the caller's transaction. A retired row
/// whose unique key the survivor already holds is dropped only under a KeepSurvivor rule, after its
/// dependents follow the survivor's row; anything else refuses and skips that table's writes.
/// </summary>
public sealed class EquityReferenceRekeyer
{
    private const int MaxDepth = 4;
    private const int CommandTimeoutSeconds = 600;

    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private readonly EquityReferenceCatalog _catalog;
    private readonly EquityReferencePolicy _policy;
    private readonly EquityIssuerMergeReport _report;
    private readonly List<(string Table, string Column, string Stage)> _staged = [];
    private readonly HashSet<EquityColumnReference> _unruled = [];

    private EquityReferenceRekeyer(
        DbConnection connection,
        DbTransaction transaction,
        EquityReferenceCatalog catalog,
        EquityReferencePolicy policy,
        EquityIssuerMergeReport report
    )
    {
        _connection = connection;
        _transaction = transaction;
        _catalog = catalog;
        _policy = policy;
        _report = report;
    }

    public static async Task<EquityReferenceRekeyer> Create(
        DatabaseFacade database,
        EquityReferencePolicy policy,
        EquityIssuerMergeReport report,
        CancellationToken cancellationToken
    )
    {
        var transaction =
            database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Rekeying requires the caller's transaction.");
        var connection = database.GetDbConnection();
        var catalog = await EquityReferenceCatalog.Load(
            connection,
            transaction,
            cancellationToken
        );
        return new EquityReferenceRekeyer(connection, transaction, catalog, policy, report);
    }

    /// <summary>Every column that holds an issuer key: foreign keys plus same-suffixed uuid columns.</summary>
    public IReadOnlyList<EquityColumnReference> IssuerReferences(
        string issuerTable,
        string issuerKey,
        string issuerColumnSuffix
    )
    {
        var references = _catalog.IssuerReferences(issuerTable, issuerKey, issuerColumnSuffix);
        foreach (var (reference, constrained) in references)
        {
            // Without a foreign key nothing proves the column means this issuer, so a rule must say so.
            if (!constrained && _policy.Rule(reference.Table) == null)
                _unruled.Add(reference);
        }
        return references.Select(row => row.Reference).ToList();
    }

    public Task Rekey(
        EquityColumnReference reference,
        Guid from,
        Guid to,
        CancellationToken cancellationToken
    ) => RekeyCore(reference, from, to, 0, cancellationToken);

    /// <summary>Re-inserts rows lifted out for insert-time identity triggers, now owned by the survivor.</summary>
    public async Task Reattach(Guid to, CancellationToken cancellationToken)
    {
        foreach (var (table, column, stage) in _staged)
        {
            var columns = _catalog.InsertableColumns(table);
            var values = string.Join(", ", columns.Select(name => name == column ? "@to" : Quote(name)));
            await Execute(
                $"INSERT INTO {Quote(table)} ({string.Join(", ", columns.Select(Quote))}) SELECT {values} FROM {stage}",
                cancellationToken,
                ("to", to)
            );
        }
        _staged.Clear();
    }

    public Task<long> Count(EquityColumnReference reference, Guid key, CancellationToken token) =>
        _catalog.HasColumn(reference.Table, reference.Column)
            ? Scalar(
                $"SELECT count(*) FROM {Quote(reference.Table)} WHERE {Quote(reference.Column)} = @key",
                token,
                ("key", key)
            )
            : Task.FromResult(0L);

    public async Task<int> Execute(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters
    )
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task RekeyCore(
        EquityColumnReference reference,
        Guid from,
        Guid to,
        int depth,
        CancellationToken cancellationToken
    )
    {
        var (table, column) = reference;
        if (!_catalog.HasColumn(table, column))
            return;
        var count = await Count(reference, from, cancellationToken);
        if (count == 0)
            return;
        var rule = _policy.Rule(table);
        if (_unruled.Contains(reference))
        {
            Refuse($"{table}.{column} holds {count} rows but carries no foreign key or merge rule.");
            return;
        }
        if (rule?.RefuseAnyRow == true)
        {
            Refuse($"{table}.{column} holds {count} rows that never follow a merge.");
            return;
        }
        if (depth > MaxDepth)
        {
            Refuse($"{table}.{column} sits deeper than {MaxDepth} dependent levels.");
            return;
        }
        if (_catalog.ColumnType(table, column) != "uuid")
        {
            Refuse($"{table}.{column} is not a uuid column.");
            return;
        }

        var key = _catalog.PrimaryKey(table);
        var duplicates = await Duplicates(table, column, key, from, to, cancellationToken);
        if (duplicates == null)
            return;
        if (duplicates.Count > 0)
        {
            if (rule?.OnCollision != EquityReferenceCollision.KeepSurvivor)
            {
                Refuse($"{table}: {duplicates.Count} rows duplicate the survivor's on a unique key.");
                return;
            }
            foreach (var (retired, survivor) in duplicates)
            {
                if (!await DropDuplicate(table, key, rule, retired, survivor, depth, cancellationToken))
                    return;
            }
        }

        var change = _report.Change(table, column);
        change.DroppedDuplicates += duplicates.Count;
        change.Moved += count - duplicates.Count;
        if (count == duplicates.Count)
            return;
        if (rule?.Reinsert == true && depth == 0)
        {
            var stage = "equity_merge_stage_" + Guid.NewGuid().ToString("N");
            await Execute(
                $"CREATE TEMP TABLE {stage} ON COMMIT DROP AS SELECT * FROM {Quote(table)} WHERE {Quote(column)} = @from",
                cancellationToken,
                ("from", from)
            );
            await Execute(
                $"DELETE FROM {Quote(table)} WHERE {Quote(column)} = @from",
                cancellationToken,
                ("from", from)
            );
            _staged.Add((table, column, stage));
            return;
        }
        await Execute(
            $"UPDATE {Quote(table)} SET {Quote(column)} = @to WHERE {Quote(column)} = @from",
            cancellationToken,
            ("to", to),
            ("from", from)
        );
    }

    // Retired rows whose unique key the survivor's rows already hold, keyed by the retired row's
    // primary key (or ctid when the key is composite) and mapped to the survivor's primary key.
    private async Task<Dictionary<string, string>> Duplicates(
        string table,
        string column,
        string key,
        Guid from,
        Guid to,
        CancellationToken cancellationToken
    )
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var index in _catalog.UniqueOn(table, column))
        {
            if (index.Columns.Any(string.IsNullOrEmpty))
            {
                Refuse($"{table}: unique index {index.Name} uses an expression.");
                return null;
            }
            var others = index.Columns.Where(name => name != column).ToList();
            var selected = string.Concat(others.Select(name => ", " + Quote(name)));
            var predicate = index.Predicate == null ? "" : $" AND ({index.Predicate})";
            var match =
                others.Count == 0
                    ? "TRUE"
                    : string.Join(
                        " AND ",
                        others.Select(name =>
                            index.NullsNotDistinct
                                ? $"r.{Quote(name)} IS NOT DISTINCT FROM s.{Quote(name)}"
                                : $"r.{Quote(name)} = s.{Quote(name)}"
                        )
                    );
            var retiredId = key == null ? "ctid::text" : Quote(key) + "::text";
            var survivorId = key == null ? "NULL::text" : Quote(key) + "::text";
            var sql = $"""
                SELECT r.id, s.id
                FROM (SELECT {retiredId} AS id{selected} FROM {Quote(table)} WHERE {Quote(column)} = @from{predicate}) r
                JOIN (SELECT {survivorId} AS id{selected} FROM {Quote(table)} WHERE {Quote(column)} = @to{predicate}) s
                    ON {match}
                """;
            await using var command = Command(sql, [("from", from), ("to", to)]);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var retired = reader.GetString(0);
                var survivor = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (result.TryGetValue(retired, out var known) && known != survivor)
                {
                    Refuse($"{table}: a retired row duplicates two different survivor rows.");
                    return null;
                }
                result[retired] = survivor;
            }
        }
        return result;
    }

    private async Task<bool> DropDuplicate(
        string table,
        string key,
        EquityReferenceRule rule,
        string retired,
        string survivor,
        int depth,
        CancellationToken cancellationToken
    )
    {
        var refusals = _report.Refusals.Count;
        var children = _catalog.ChildrenOf(table).ToList();
        if (key == null || _catalog.ColumnType(table, key) != "uuid")
        {
            // Without a uuid key nothing can follow the survivor's row, so the row must stand alone.
            if (children.Count > 0 || rule.SoftReferences.Count > 0)
            {
                Refuse($"{table}: a duplicate row without a uuid key may have dependents.");
                return false;
            }
            await Execute(
                key == null
                    ? $"DELETE FROM {Quote(table)} WHERE ctid = @row::tid"
                    : $"DELETE FROM {Quote(table)} WHERE {Quote(key)}::text = @row",
                cancellationToken,
                ("row", retired)
            );
            return true;
        }

        var retiredKey = Guid.Parse(retired);
        var survivorKey = Guid.Parse(survivor);
        foreach (var child in children)
        {
            if (child.ColumnCount != 1)
            {
                Refuse($"{table}: {child.Table} references it through a composite key.");
                return false;
            }
            // Cascade and set-null children follow the database's own delete rule.
            if (child.OnDelete is not ('a' or 'r'))
                continue;
            var reference = new EquityColumnReference(child.Table, child.Column);
            var dependents = await Count(reference, retiredKey, cancellationToken);
            if (dependents == 0)
                continue;
            if (!rule.RekeyDependents)
            {
                Refuse($"{table}: a duplicate row has {dependents} dependent rows in {child.Table}.");
                return false;
            }
            await RekeyCore(reference, retiredKey, survivorKey, depth + 1, cancellationToken);
        }
        foreach (var reference in rule.SoftReferences)
            await RekeyCore(reference, retiredKey, survivorKey, depth + 1, cancellationToken);
        if (_report.Refusals.Count != refusals)
            return false;

        _report.Remaps.Add(new EquityKeyRemap(table, retiredKey, survivorKey));
        await Execute(
            $"DELETE FROM {Quote(table)} WHERE {Quote(key)} = @key",
            cancellationToken,
            ("key", retiredKey)
        );
        return true;
    }

    private async Task<long> Scalar(
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters
    )
    {
        await using var command = Command(sql, parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private DbCommand Command(string sql, (string Name, object Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.Transaction = _transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private void Refuse(string reason) => _report.Refusals.Add(reason);

    internal static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
