namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Accumulates one quarter's worth of positions filed under a CUSIP the import could not resolve,
/// so the whole data set is counted before a single row is written.
/// </summary>
public class UnmappedCusipTally
{
    public string IssuerName { get; private set; }

    public int Positions { get; private set; }

    public long FiledValue { get; private set; }

    public void Add(string issuerName, decimal filedDollars)
    {
        Positions++;

        // Filers spell the same issuer differently across rows; the first non-empty name is as
        // good as any and avoids the tally depending on row order for anything but a label.
        if (IssuerName == null && !string.IsNullOrWhiteSpace(issuerName))
        {
            IssuerName = issuerName;
        }

        if (filedDollars <= 0)
        {
            return;
        }

        var total = FiledValue + filedDollars;
        FiledValue = total > long.MaxValue ? long.MaxValue : (long)total;
    }
}
