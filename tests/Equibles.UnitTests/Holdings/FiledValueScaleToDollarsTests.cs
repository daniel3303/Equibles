using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class FiledValueScaleToDollarsTests
{
    [Fact]
    public void ToDollars_FiledBeforeTheModernization_ScalesThousandsUp()
    {
        // 13F values were filed in thousands until the SEC's 2022 modernization took effect on
        // 2023-01-03. The audit that compares a derived value against its filed one is only
        // meaningful once both are dollars — skip this and every pre-2023 position looks like it
        // disagrees by 1,000x, which would bury the real basis errors under ~20 years of noise.
        FiledValueScale.ToDollars(868, new DateOnly(2022, 12, 31)).Should().Be(868_000m);
    }

    [Fact]
    public void ToDollars_FiledOnTheEffectiveDate_IsAlreadyDollars()
    {
        // The boundary is inclusive: a filing dated 2023-01-03 is already whole dollars. Getting
        // this off by a day inflates a whole quarter's filings by 1,000x.
        FiledValueScale.ToDollars(868_524, new DateOnly(2023, 1, 3)).Should().Be(868_524m);
    }

    [Fact]
    public void ToDollars_FiledAfterTheModernization_IsAlreadyDollars()
    {
        FiledValueScale.ToDollars(868_524, new DateOnly(2024, 8, 14)).Should().Be(868_524m);
    }
}
