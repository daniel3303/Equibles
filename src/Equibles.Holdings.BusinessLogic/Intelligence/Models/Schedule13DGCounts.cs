namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// Schedule 13D and 13G filers whose newest filing on a security by a date reports a stake of 5%
/// or more, the 13G filers split by the Rule 13d-1 paragraph they file under when it is known.
/// </summary>
public sealed record Schedule13DGCounts
{
    public int Schedule13D { get; init; }
    public int Schedule13G { get; init; }
    public int QualifiedInstitutions { get; init; }
    public int PassiveInvestors { get; init; }
    public int ExemptInvestors { get; init; }
}
