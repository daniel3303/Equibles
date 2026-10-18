using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// What decides a filer's bucket in one quarter: its current regulatory profile, its measurements
/// in the quarter's measured quarter (null when not measured) and its filed address code.
/// </summary>
public sealed record HolderMixHolder
{
    public bool Profiled { get; init; }
    public HolderEntityType EntityType { get; init; }
    public HolderClientBase ClientBase { get; init; }
    public bool? AdvisesPrivateFunds { get; init; }
    public PrivateFundFocus PrivateFundFocus { get; init; }
    public HolderStyleBand? StyleBand { get; init; }
    public HolderTurnoverBand? TurnoverBand { get; init; }
    public HolderHorizonBand? HorizonBand { get; init; }
    public long? EquityAum { get; init; }
    public string StateOrCountry { get; init; }
}
