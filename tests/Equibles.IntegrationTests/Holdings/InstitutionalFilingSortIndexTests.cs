using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the composite index that makes the "latest 13F filings" feed an
/// index-served scan. <c>GetRecentFilings</c> orders by
/// <c>FilingDate DESC, AccessionNumber DESC</c> and pages the
/// <see cref="Equibles.Holdings.Data.Models.InstitutionalFiling"/> rollup; with no
/// covering index Postgres full-sorts the whole rollup on every request, which
/// exceeded the 30s command timeout and 500'd the page on every load (#3565). A
/// composite over <c>(FilingDate, AccessionNumber)</c> turns that sort into a
/// backward index scan over just the page's rows.
///
/// <para>
/// The fix is pure timing, so it has no result-level contract — this instead pins
/// the fix's mechanism: the migration must physically create the
/// <c>(FilingDate, AccessionNumber)</c> index on the production schema. The fixture
/// applies the real migrations, so the assertion fails on the pre-fix schema (only a
/// single-column <c>FilingDate</c> index existed) and passes once the migration runs.
/// </para>
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalFilingSortIndexTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;

    public InstitutionalFilingSortIndexTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Schema_HasCompositeFilingDateThenAccessionNumberIndex()
    {
        await using var ctx = _fixture.CreateDbContext();

        // pg_indexes.indexdef spells the index out, e.g.
        //   CREATE INDEX "..." ON public."InstitutionalFiling"
        //   USING btree ("FilingDate", "AccessionNumber")
        // Scalar SqlQueryRaw requires the column to be named "Value".
        var indexDefs = await ctx
            .Database.SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE tablename = 'InstitutionalFiling'
                """
            )
            .ToListAsync();

        // FilingDate must be the leading column (so the backward scan satisfies the
        // ORDER BY) with AccessionNumber as the tie-breaker; the single-column
        // AccessionNumber unique index does not match this pair.
        indexDefs
            .Should()
            .ContainSingle(def => def.Contains("\"FilingDate\", \"AccessionNumber\""));
    }
}
