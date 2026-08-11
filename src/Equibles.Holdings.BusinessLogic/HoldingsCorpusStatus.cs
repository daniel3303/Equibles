namespace Equibles.Holdings.BusinessLogic;

public sealed record HoldingsCorpusStatus(
    DateOnly CoverageStartDate,
    bool IsWithinCoverage,
    bool ComparisonAvailable,
    string ComparisonUnavailableReason
);
