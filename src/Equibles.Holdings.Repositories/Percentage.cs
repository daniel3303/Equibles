namespace Equibles.Holdings.Repositories;

public static class Percentage
{
    // Guard the divide so a zero (or empty) total yields 0% rather than NaN/Infinity.
    public static double Of(double value, double total) => total > 0 ? value / total * 100.0 : 0;
}
