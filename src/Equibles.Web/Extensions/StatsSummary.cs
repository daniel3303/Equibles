namespace Equibles.Web.Extensions;

public readonly record struct StatsSummary(
    decimal? Mean,
    decimal? Median,
    decimal? Min,
    decimal? Max,
    decimal? StdDev
);
