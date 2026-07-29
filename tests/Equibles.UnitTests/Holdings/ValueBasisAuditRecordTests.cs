using Equibles.Holdings.HostedService.Models;

namespace Equibles.UnitTests.Holdings;

public class ValueBasisAuditRecordTests
{
    [Fact]
    public void Record_DerivedAndFiledCloseTogether_CountsTheComparisonButNoDisagreement()
    {
        // Filed and derived values differ routinely: the price may come from a lookback day, and
        // a filer marks its own book. The audit has to stay quiet for those or it is noise nobody
        // reads, and the one signal that matters gets ignored with it.
        var audit = new ValueBasisAudit();

        audit.Record(
            "09077B104",
            new DateOnly(2024, 6, 30),
            633_959,
            868_523,
            868_524m,
            isOption: false
        );

        audit.Compared.Should().Be(1);
        audit.Disagreed.Should().Be(0);
        audit.Samples.Should().BeEmpty();
    }

    [Fact]
    public void Record_DerivedFiftyTimesTheFiledValue_FlagsItAndKeepsTheEvidence()
    {
        // The BioAtla shape. A 50x gap cannot be mark drift, so it is exactly the tell that a
        // basis is wrong somewhere upstream — a split never captured, a depositary ratio we do
        // not model. The sample is kept because the RATIO between the two figures is the
        // diagnosis; a bare count would say something is broken without saying what.
        var audit = new ValueBasisAudit();

        audit.Record(
            "09077B104",
            new DateOnly(2024, 6, 30),
            633_959,
            43_426_191,
            868_524m,
            isOption: false
        );

        audit.Compared.Should().Be(1);
        audit.Disagreed.Should().Be(1);
        audit.Samples.Should().ContainSingle();
        audit.Samples[0].Cusip.Should().Be("09077B104");
        audit.Samples[0].Shares.Should().Be(633_959);
        audit.Samples[0].DerivedValue.Should().Be(43_426_191);
        audit.Samples[0].FiledValue.Should().Be(868_524m);
    }

    [Fact]
    public void Record_FiledValueFarAboveDerived_FlagsItToo()
    {
        // A forward split understates rather than overstates, so the check has to be symmetric.
        // Testing only one direction would leave the far more common failure unreported.
        var audit = new ValueBasisAudit();

        audit.Record(
            "67066G104",
            new DateOnly(2023, 12, 31),
            900_000,
            44_568_000,
            445_680_000m,
            isOption: false
        );

        audit.Disagreed.Should().Be(1);
    }

    [Fact]
    public void Record_NothingWasDerived_IsNotAComparison()
    {
        // A row we could not value, or a Schedule 13D/G filing that reports no value at all, has
        // nothing to compare. Counting those as agreement would let the audit read healthy
        // precisely when the pipeline had stopped producing values.
        var audit = new ValueBasisAudit();

        audit.Record("09077B104", new DateOnly(2024, 6, 30), 633_959, 0, 868_524m, isOption: false);
        audit.Record("09077B104", new DateOnly(2024, 6, 30), 633_959, 868_523, 0m, isOption: false);

        audit.Compared.Should().Be(0);
        audit.Disagreed.Should().Be(0);
    }

    [Fact]
    public void Record_ManyDisagreements_CountsThemAllButBoundsTheEvidence()
    {
        // A whole data set can be wrong at once — a re-import after a split touches every filer
        // holding that stock. The count has to stay exact while the samples stay bounded, or one
        // bad import writes a multi-megabyte log line.
        var audit = new ValueBasisAudit();

        for (var i = 0; i < 50; i++)
        {
            audit.Record(
                $"CUSIP{i}",
                new DateOnly(2024, 6, 30),
                1_000,
                50_000,
                1_000m,
                isOption: false
            );
        }

        audit.Compared.Should().Be(50);
        audit.Disagreed.Should().Be(50);
        audit.Samples.Should().HaveCount(10);
    }
}
