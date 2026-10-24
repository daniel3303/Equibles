using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Equibles.Data.Extensions;

// Combines predicates written as separate lambdas into one lambda over a single parameter, so EF
// translates the result as one WHERE clause instead of rejecting a body that still references the
// other lambdas' parameters.
public static class PredicateExpressions
{
    public static Expression<Func<T, bool>> AnyOf<T>(
        params Expression<Func<T, bool>>[] predicates
    ) => Combine(predicates, Expression.OrElse);

    public static Expression<Func<T, bool>> AllOf<T>(
        params Expression<Func<T, bool>>[] predicates
    ) => Combine(predicates, Expression.AndAlso);

    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>>[] predicates,
        Func<Expression, Expression, BinaryExpression> join
    )
    {
        if (predicates.Length == 0)
        {
            throw new ArgumentException("At least one predicate is required.", nameof(predicates));
        }

        var parameter = predicates[0].Parameters[0];
        var body = predicates
            .Select(predicate =>
                ReplacingExpressionVisitor.Replace(
                    predicate.Parameters[0],
                    parameter,
                    predicate.Body
                )
            )
            .Aggregate((left, right) => join(left, right));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}
