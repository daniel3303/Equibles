namespace Equibles.CommonStocks.BusinessLogic.Merge;

public sealed class EquityIssuerMergeRequest
{
    public Guid RetiredIssuerId { get; init; }
    public Guid SurvivingIssuerId { get; init; }
    public string EvidenceKind { get; init; }
    public IReadOnlyList<Guid> EvidenceSourceRecordIds { get; init; } = [];
    public string MergedBy { get; init; }

    // Accession prefixes of retired facts that are withdrawn because the survivor's source governs them.
    public IReadOnlyList<string> WithdrawnFactPrefixes { get; init; } = [];

    // False runs every step and rolls back, so a preview reports exactly what a merge would do.
    public bool Execute { get; init; }
}

/// <summary>Rows of one table that followed (or would follow) a merge.</summary>
public sealed class EquityReferenceChange
{
    public string Table { get; init; }
    public string Column { get; init; }
    public long Moved { get; set; }

    // Retired rows dropped because the survivor holds the same unique key.
    public long DroppedDuplicates { get; set; }
}

/// <summary>A dropped duplicate row and the survivor row that replaced it.</summary>
public sealed record EquityKeyRemap(string Table, Guid RetiredKey, Guid SurvivorKey);

public sealed class EquityIssuerMergeReport
{
    public Guid RetiredIssuerId { get; init; }
    public Guid SurvivingIssuerId { get; init; }
    public bool Executed { get; set; }
    public int WithdrawnFacts { get; set; }
    public List<string> Refusals { get; init; } = [];
    public List<EquityReferenceChange> Changes { get; init; } = [];
    public List<EquityKeyRemap> Remaps { get; init; } = [];

    public bool IsClean => Refusals.Count == 0;

    internal EquityReferenceChange Change(string table, string column)
    {
        var change = Changes.FirstOrDefault(row => row.Table == table && row.Column == column);
        if (change != null)
            return change;
        change = new EquityReferenceChange { Table = table, Column = column };
        Changes.Add(change);
        return change;
    }
}
