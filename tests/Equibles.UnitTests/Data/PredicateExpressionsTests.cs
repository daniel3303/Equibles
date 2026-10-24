using System.Linq.Expressions;
using Equibles.Data.Extensions;

namespace Equibles.UnitTests.Data;

public class PredicateExpressionsTests
{
    private sealed record Row(int Number, string Name);

    private static readonly Row[] Rows = [new(1, "a"), new(2, "b"), new(3, "c"), new(4, "a")];

    [Fact]
    public void AnyOf_LambdasWithTheirOwnParameters_MatchesRowsSatisfyingAnyOfThem()
    {
        Expression<Func<Row, bool>> small = r => r.Number < 2;
        Expression<Func<Row, bool>> namedC = x => x.Name == "c";

        var combined = PredicateExpressions.AnyOf(small, namedC);

        combined.Parameters.Should().ContainSingle();
        Rows.AsQueryable().Where(combined).Select(r => r.Number).Should().Equal(1, 3);
    }

    [Fact]
    public void AllOf_LambdasWithTheirOwnParameters_MatchesRowsSatisfyingAllOfThem()
    {
        Expression<Func<Row, bool>> namedA = r => r.Name == "a";
        Expression<Func<Row, bool>> large = x => x.Number > 2;

        var combined = PredicateExpressions.AllOf(namedA, large);

        Rows.AsQueryable().Where(combined).Select(r => r.Number).Should().Equal(4);
    }

    [Fact]
    public void AnyOf_NoPredicates_Throws()
    {
        var act = () => PredicateExpressions.AnyOf<Row>();

        act.Should().Throw<ArgumentException>();
    }
}
