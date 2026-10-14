namespace Equibles.CommonStocks.BusinessLogic.Merge;

/// <summary>What a merge does with a retired row whose unique key the survivor already holds.</summary>
public enum EquityReferenceCollision
{
    Refuse,
    KeepSurvivor,
}

/// <summary>A column that holds another table's key, with or without a foreign key.</summary>
public sealed record EquityColumnReference(string Table, string Column);

/// <summary>
/// How one table's rows follow a merge. A table without a rule moves its rows and refuses any
/// unique-key collision with the survivor's rows.
/// </summary>
public sealed record EquityReferenceRule(string Table)
{
    public EquityReferenceCollision OnCollision { get; init; } = EquityReferenceCollision.Refuse;

    // A dropped duplicate's restrict-bound children move to the survivor's row only when allowed.
    public bool RekeyDependents { get; init; }

    // Columns that hold this table's key without a foreign key; they follow a dropped duplicate.
    public IReadOnlyList<EquityColumnReference> SoftReferences { get; init; } = [];

    // Rows that must never follow a merge, such as issuer-scoped prices observed on another venue.
    public bool RefuseAnyRow { get; init; }

    // Rows guarded by insert-time identity triggers are lifted out and re-inserted after the securities move.
    public bool Reinsert { get; init; }
}

/// <summary>Supplies merge rules for tables a module owns.</summary>
public interface IEquityReferenceRules
{
    IEnumerable<EquityReferenceRule> Rules { get; }
}

public sealed class EquityReferencePolicy
{
    private readonly Dictionary<string, EquityReferenceRule> _rules;

    public EquityReferencePolicy(IEnumerable<EquityReferenceRule> rules)
    {
        _rules = new Dictionary<string, EquityReferenceRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (!_rules.TryAdd(rule.Table, rule))
                throw new InvalidOperationException($"Two merge rules name table {rule.Table}.");
        }
    }

    public EquityReferenceRule Rule(string table) => _rules.GetValueOrDefault(table);

    public IReadOnlyCollection<EquityReferenceRule> Rules => _rules.Values;
}
