namespace Equibles.Search.Abstractions;

/// <summary>
/// Base for providers backed by an <see cref="IQueryable{T}"/> repository search. Owns the common
/// pipeline — cap to <see cref="SearchRequest.MaxPerProvider"/>, materialise, project, build the
/// group — so a concrete provider only declares its category, query and per-row projection.
/// <para>
/// This package stays EF-free: materialisation is deferred to <see cref="Materialize"/>, which the
/// module implements as a one-line <c>ToListAsync</c> where EF Core is already referenced.
/// </para>
/// </summary>
public abstract class QueryableSearchProvider<TEntity> : ISearchProvider
{
    public abstract string Category { get; }

    public abstract int Order { get; }

    /// <summary>The (ideally ordered) query for this request, before the result cap is applied.</summary>
    protected abstract IQueryable<TEntity> Filter(SearchRequest request);

    /// <summary>Maps one matched entity to a <see cref="SearchHit"/>.</summary>
    protected abstract SearchHit Project(TEntity entity);

    /// <summary>Runs the capped query. Implemented per module as <c>query.ToListAsync(token)</c>.</summary>
    protected abstract Task<List<TEntity>> Materialize(
        IQueryable<TEntity> query,
        CancellationToken cancellationToken
    );

    public async Task<SearchResultGroup> Search(
        SearchRequest request,
        CancellationToken cancellationToken
    )
    {
        var entities = await Materialize(
            Filter(request).Take(request.MaxPerProvider),
            cancellationToken
        );

        return new SearchResultGroup
        {
            Category = Category,
            Order = Order,
            Hits = entities.Select(Project).ToList(),
        };
    }
}
