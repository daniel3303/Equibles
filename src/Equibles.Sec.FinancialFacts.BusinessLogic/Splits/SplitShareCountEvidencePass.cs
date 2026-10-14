namespace Equibles.Sec.FinancialFacts.BusinessLogic.Splits;

/// <summary>One reconciliation pass: the rows whose evidence changed and the issuers it could not record.</summary>
public sealed record SplitShareCountEvidencePass(
    int Changed,
    IReadOnlyList<(Guid IssuerId, Exception Error)> Failures
);
