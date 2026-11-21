namespace Equibles.Finra.Mcp.Captures;

// One settlement of a GetShortInterest answer: the restated figures behind its cells and each cell as printed.
public sealed record ShortInterestRow(
    DateOnly SettlementDate,
    long Position,
    string PositionText,
    long Change,
    string ChangeText,
    long? AverageDailyVolume,
    string AverageDailyVolumeText,
    decimal? DaysToCover,
    string DaysToCoverText
);
