using System.Reflection;
using Equibles.Holdings.BusinessLogic;

namespace Equibles.UnitTests.Holdings;

public class BacktestPriceLoaderForwardFillExactDateTests
{
    // ForwardFill's contract is "largest [close] on or before the requested date".
    // On a date that EXACTLY equals an entry, that entry's own close is the answer
    // — the boundary is inclusive (Date <= date). The sibling pin queries a date
    // strictly between two entries, which a flipped `<=`→`<` regression survives
    // (the prior entry still matches a strictly-greater query). An exact-date query
    // is the only case that distinguishes the two: with `<` it would leak the
    // PRIOR entry's close instead of the matching day's.
    [Fact]
    public void ForwardFill_DateExactlyEqualsAnEntry_ReturnsThatEntrysCloseNotThePrior()
    {
        var loaderType = typeof(FundScoringManager).Assembly.GetType(
            "Equibles.Holdings.BusinessLogic.BacktestPriceLoader"
        );
        var priceRowType = loaderType.GetNestedType("PriceRow");
        var priceRowCtor = priceRowType.GetConstructor([
            typeof(Guid),
            typeof(DateOnly),
            typeof(decimal),
        ]);

        var stockId = Guid.NewGuid();
        var series = Array.CreateInstance(priceRowType, 3);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 1), 100m]), 0);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 1, 15), 200m]), 1);
        series.SetValue(priceRowCtor.Invoke([stockId, new DateOnly(2025, 2, 1), 300m]), 2);

        var forwardFill = loaderType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == "ForwardFill"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType == priceRowType.MakeArrayType()
            );

        var result = (decimal?)forwardFill.Invoke(null, [series, new DateOnly(2025, 1, 15)]);

        result.Should().Be(200m);
    }
}
