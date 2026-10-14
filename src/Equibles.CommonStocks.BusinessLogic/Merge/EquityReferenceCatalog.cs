using System.Data.Common;

namespace Equibles.CommonStocks.BusinessLogic.Merge;

/// <summary>The live schema's keys, read once per merge so every rule applies to what the database enforces.</summary>
internal sealed class EquityReferenceCatalog
{
    internal sealed record ForeignKey(
        string Table,
        string Column,
        string ReferencedTable,
        string ReferencedColumn,
        char OnDelete,
        int ColumnCount
    );

    internal sealed record UniqueIndex(
        string Table,
        string Name,
        IReadOnlyList<string> Columns,
        string Predicate,
        bool NullsNotDistinct,
        bool Primary
    );

    internal sealed record Column(string Table, string Name, string Type, bool Generated);

    private const string ForeignKeySql = """
        SELECT child.relname::text, ca.attname::text, parent.relname::text, pa.attname::text,
            c.confdeltype::text, cardinality(c.conkey)
        FROM pg_constraint c
        JOIN pg_class child ON child.oid = c.conrelid
        JOIN pg_namespace n ON n.oid = child.relnamespace
        JOIN pg_class parent ON parent.oid = c.confrelid
        JOIN pg_attribute ca ON ca.attrelid = c.conrelid AND ca.attnum = c.conkey[1]
        JOIN pg_attribute pa ON pa.attrelid = c.confrelid AND pa.attnum = c.confkey[1]
        WHERE c.contype = 'f' AND n.nspname = 'public'
        """;

    // Expression keys read as an empty column name so callers refuse them instead of guessing.
    private const string UniqueIndexSql = """
        SELECT t.relname::text, i.relname::text,
            ARRAY(SELECT coalesce(a.attname::text, '')
                FROM unnest(x.indkey::int2[]) WITH ORDINALITY k(attnum, ord)
                LEFT JOIN pg_attribute a ON a.attrelid = x.indrelid AND a.attnum = k.attnum
                WHERE k.ord <= x.indnkeyatts ORDER BY k.ord),
            pg_get_expr(x.indpred, x.indrelid), x.indnullsnotdistinct, x.indisprimary
        FROM pg_index x
        JOIN pg_class t ON t.oid = x.indrelid
        JOIN pg_class i ON i.oid = x.indexrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        WHERE x.indisunique AND n.nspname = 'public' AND t.relkind IN ('r', 'p')
        """;

    private const string ColumnSql = """
        SELECT c.relname::text, a.attname::text, format_type(a.atttypid, a.atttypmod), a.attgenerated <> ''
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p') AND a.attnum > 0 AND NOT a.attisdropped
        ORDER BY c.relname, a.attnum
        """;

    public IReadOnlyList<ForeignKey> ForeignKeys { get; private init; }
    public IReadOnlyList<UniqueIndex> UniqueIndexes { get; private init; }
    public IReadOnlyList<Column> Columns { get; private init; }

    public static async Task<EquityReferenceCatalog> Load(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        var foreignKeys = await Read(
            connection,
            transaction,
            ForeignKeySql,
            reader => new ForeignKey(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)[0],
                reader.GetInt32(5)
            ),
            cancellationToken
        );
        var indexes = await Read(
            connection,
            transaction,
            UniqueIndexSql,
            reader => new UniqueIndex(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<string[]>(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5)
            ),
            cancellationToken
        );
        var columns = await Read(
            connection,
            transaction,
            ColumnSql,
            reader => new Column(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)
            ),
            cancellationToken
        );
        return new EquityReferenceCatalog
        {
            ForeignKeys = foreignKeys,
            UniqueIndexes = indexes,
            Columns = columns,
        };
    }

    public bool HasColumn(string table, string column) =>
        Columns.Any(row => row.Table == table && row.Name == column);

    public string ColumnType(string table, string column) =>
        Columns.SingleOrDefault(row => row.Table == table && row.Name == column)?.Type;

    public IReadOnlyList<string> InsertableColumns(string table) =>
        Columns.Where(row => row.Table == table && !row.Generated).Select(row => row.Name).ToList();

    // The single column of the table's primary key, or null when the key is composite.
    public string PrimaryKey(string table)
    {
        var primary = UniqueIndexes.SingleOrDefault(row => row.Table == table && row.Primary);
        return primary is { Columns.Count: 1 } ? primary.Columns[0] : null;
    }

    public IEnumerable<UniqueIndex> UniqueOn(string table, string column) =>
        UniqueIndexes.Where(row => row.Table == table && row.Columns.Contains(column));

    public IEnumerable<ForeignKey> ChildrenOf(string table) =>
        ForeignKeys.Where(row => row.ReferencedTable == table);

    /// <summary>Foreign keys to the issuer key plus same-named columns that carry it without one.</summary>
    public IReadOnlyList<(EquityColumnReference Reference, bool Constrained)> IssuerReferences(
        string issuerTable,
        string issuerKey,
        string issuerColumnSuffix
    )
    {
        var constrained = ForeignKeys
            .Where(row =>
                row.ReferencedTable == issuerTable
                && row.ReferencedColumn == issuerKey
                && row.ColumnCount == 1
            )
            .Select(row => new EquityColumnReference(row.Table, row.Column))
            .Distinct()
            .ToList();
        var unconstrained = Columns
            .Where(row =>
                row.Table != issuerTable
                && row.Type == "uuid"
                && row.Name.EndsWith(issuerColumnSuffix, StringComparison.Ordinal)
            )
            .Select(row => new EquityColumnReference(row.Table, row.Name))
            .Where(reference => !constrained.Contains(reference))
            .ToList();
        return constrained
            .Select(reference => (reference, true))
            .Concat(unconstrained.Select(reference => (reference, false)))
            .OrderBy(row => row.Item1.Table, StringComparer.Ordinal)
            .ThenBy(row => row.Item1.Column, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<List<T>> Read<T>(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(map(reader));
        return rows;
    }
}
