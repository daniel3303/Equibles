namespace Equibles.Holdings.HostedService.Models;

/// <summary>
/// Running tally, for one import, of how the values this pipeline derives compare to the values
/// filers actually reported.
/// </summary>
/// <remarks>
/// <para>
/// The derived value is the published one, so nothing here changes what is stored — this exists
/// because a silent units error is the failure mode that produced a $43.4M BioAtla position against
/// a filed $868,524 and went unnoticed for months. Filed and derived figures legitimately differ a
/// little (the price may come from a lookback day, and a filer marks its own book), so only a gross
/// disagreement is counted: those are the tell of a basis problem — a missing split, an unmapped
/// depositary ratio, a share count in the wrong units — rather than of ordinary mark drift.
/// </para>
/// <para>
/// Option rows are tallied separately and never sampled. Our derivation is always the underlying's
/// notional (shares × close, which is what the 13F rules require reported), but filers report
/// option values inconsistently — some file the premium instead — so for those rows the two
/// figures are often different QUANTITIES rather than a units error. Measured on the first
/// production re-import they disagreed ~14% of the time against ~1.9% for common stock, a quarter
/// of all flags; folded into one number they buried the signal this audit exists to surface.
/// </para>
/// </remarks>
public class ValueBasisAudit
{
    /// <summary>
    /// How far apart the two figures must be before the disagreement is treated as structural. No
    /// mark drift reaches 2×; the cases worth surfacing are off by a split ratio or a depositary
    /// ratio, which start at 3× and run to 1,000×.
    /// </summary>
    public const decimal DisagreementMultiple = 2m;

    private const int MaxSamples = 10;

    private readonly List<ValueBasisDisagreement> _samples = [];

    public int Compared { get; private set; }

    public int Disagreed { get; private set; }

    public int OptionCompared { get; private set; }

    public int OptionDisagreed { get; private set; }

    /// <summary>
    /// A bounded set of concrete disagreements, kept so the log names securities to investigate
    /// rather than only a count. Common-stock rows only: an option "disagreement" usually means
    /// the filer reported the premium, which is not something an operator can chase.
    /// </summary>
    public IReadOnlyList<ValueBasisDisagreement> Samples => _samples;

    /// <summary>
    /// Records one comparison. Both figures must be in dollars and positive; a row we could not
    /// value, or a filing that reports no value at all (Schedule 13D/G), is not a comparison and
    /// must not be passed here.
    /// </summary>
    public void Record(
        string cusip,
        DateOnly reportDate,
        long shares,
        long derived,
        decimal filed,
        bool isOption
    )
    {
        if (derived <= 0 || filed <= 0)
        {
            return;
        }

        var agrees =
            derived <= filed * DisagreementMultiple && filed <= derived * DisagreementMultiple;

        if (isOption)
        {
            OptionCompared++;
            if (!agrees)
            {
                OptionDisagreed++;
            }
            return;
        }

        Compared++;

        if (agrees)
        {
            return;
        }

        Disagreed++;
        if (_samples.Count < MaxSamples)
        {
            _samples.Add(new ValueBasisDisagreement(cusip, reportDate, shares, derived, filed));
        }
    }
}
