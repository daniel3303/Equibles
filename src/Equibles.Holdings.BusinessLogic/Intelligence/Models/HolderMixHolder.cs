using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// What decides a filer's bucket in one quarter: its current entity type, its client base and
/// private funds from the Form ADV roster current at the quarter end, its measurements in the
/// quarter's measured quarter (null when not measured) and its filed address code.
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

    /// <summary>
    /// A filer from its profile (null when not profiled) and one quarter's measurements (null
    /// fields when not measured), so the holder mix and holder flows bucket it alike.
    /// </summary>
    public static HolderMixHolder Of(
        HolderMixProfile profile,
        HolderStyleBand? styleBand,
        HolderTurnoverBand? turnoverBand,
        HolderHorizonBand? horizonBand,
        long? equityAum,
        string stateOrCountry
    ) =>
        new()
        {
            Profiled = profile != null,
            EntityType = profile?.EntityType ?? HolderEntityType.Unknown,
            ClientBase = profile?.ClientBase ?? HolderClientBase.Unknown,
            AdvisesPrivateFunds = profile?.AdvisesPrivateFunds,
            PrivateFundFocus = profile?.PrivateFundFocus ?? PrivateFundFocus.None,
            StyleBand = styleBand,
            TurnoverBand = turnoverBand,
            HorizonBand = horizonBand,
            EquityAum = equityAum,
            StateOrCountry = stateOrCountry,
        };
}
