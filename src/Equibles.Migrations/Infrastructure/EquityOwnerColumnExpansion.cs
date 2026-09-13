namespace Equibles.Migrations.Infrastructure;

internal sealed record EquityOwnerColumnExpansion(
    string Table,
    string PreviousColumn,
    string[] PrimaryKey,
    bool Required,
    (string Name, string CreateSql)[] Indexes,
    (string Name, string Definition)[] Constraints
);
