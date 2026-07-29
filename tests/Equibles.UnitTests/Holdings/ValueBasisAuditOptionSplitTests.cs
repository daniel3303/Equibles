using Equibles.Holdings.HostedService.Models;

namespace Equibles.UnitTests.Holdings;

public class ValueBasisAuditOptionSplitTests
{
    [Fact]
    public void Record_OptionRowDisagrees_TalliesApartAndNeverPollutesTheCommonStockRate()
    {
        // The production shape that motivated the split: option rows disagreed ~14% of the time
        // against ~1.9% for common stock — not because anything is wrong, but because filers
        // report option values inconsistently (some file the premium, we derive the notional the
        // rules require), so the two figures are different QUANTITIES. Folded into one tally,
        // that noise was a quarter of all flags and buried the units-error signal the audit
        // exists to surface.
        var audit = new ValueBasisAudit();

        // SPY puts at 2020-09-30: derived $167,445 (500 sh × the real $334.89 close) vs a filed
        // $37,000 premium. Correct on our side; still a disagreement in the option tally.
        audit.Record("78462F103", new DateOnly(2020, 9, 30), 500, 167_445, 37_000m, isOption: true);
        audit.Record("09077B104", new DateOnly(2020, 9, 30), 100, 10_000, 10_100m, isOption: false);

        audit.OptionCompared.Should().Be(1);
        audit.OptionDisagreed.Should().Be(1);
        audit.Compared.Should().Be(1);
        audit.Disagreed.Should().Be(0);
    }

    [Fact]
    public void Record_OptionRowDisagrees_IsNeverSampled()
    {
        // Samples are the operator's work list. An option "disagreement" usually means the filer
        // reported the premium — nothing to chase — so sampling it would fill the bounded list
        // with rows nobody can act on and crowd out the real basis errors.
        var audit = new ValueBasisAudit();

        audit.Record("78462F103", new DateOnly(2020, 9, 30), 500, 167_445, 37_000m, isOption: true);

        audit.Samples.Should().BeEmpty();
    }

    [Fact]
    public void Record_AgreeingOptionRow_CountsAsComparedOnly()
    {
        // Filers who do report the notional agree with our derivation; the tally has to show
        // that, or the option disagreement RATE reads as 100% of whatever was flagged.
        var audit = new ValueBasisAudit();

        audit.Record(
            "78462F103",
            new DateOnly(2020, 9, 30),
            500,
            167_445,
            167_445m,
            isOption: true
        );

        audit.OptionCompared.Should().Be(1);
        audit.OptionDisagreed.Should().Be(0);
    }
}
