using System.Net;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Adversarial end-to-end probe of the advisers index pagination. <c>Pagination.ClampPage</c>
/// exists solely to stop a client-supplied <c>page</c> from producing a negative SQL OFFSET —
/// its own comment notes that a negative offset is rejected by PostgreSQL (22023) and surfaces
/// as HTTP 500. But it only clamps the lower bound; <c>Pagination.Page</c> then evaluates
/// <c>Skip((page - 1) * pageSize)</c> in unchecked <c>int</c> arithmetic. For <c>page</c> =
/// <see cref="int.MaxValue"/> with a 50-row page size, <c>(2147483647 - 1) * 50</c> overflows
/// and wraps to <c>-100</c>, so the slice becomes <c>Skip(-100)</c> — the very negative offset
/// ClampPage was meant to prevent. The contract a caller relies on is "no client-supplied page
/// value yields a 500"; an absurdly large page should simply fall past the end and render an
/// empty page (HTTP 200). Driven through the real router → controller → ParadeDB.
/// </summary>
[Collection(WebHostCollection.Name)]
public class AdvisersPaginationOverflowTests
{
    private readonly WebHostFixture _fixture;

    public AdvisersPaginationOverflowTests(WebHostFixture fixture) => _fixture = fixture;

    private static Task Seed(EquiblesFinancialDbContext db)
    {
        db.Add(
            new FormAdvAdviser
            {
                Crd = 231,
                LegalName = "BNY MELLON SECURITIES CORPORATION",
                PrimaryBusinessName = "BNY MELLON",
                TotalRegulatoryAum = 2_481_367_832L,
                ReportDate = new DateOnly(2022, 4, 1),
            }
        );
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetAdvisers_PageIsIntMaxValue_DoesNotOverflowToNegativeOffset()
    {
        await _fixture.ResetAndSeedAsync(Seed);

        // int.MaxValue binds cleanly to the `int page` parameter and passes ClampPage unchanged
        // (it is > 0), then overflows (page-1)*pageSize to a negative Skip inside Pagination.Page.
        var response = await _fixture.Client.GetAsync("/advisers?page=2147483647");

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.OK,
                "a page past the end must render an empty page, not overflow to a negative OFFSET and 500"
            );
    }
}
