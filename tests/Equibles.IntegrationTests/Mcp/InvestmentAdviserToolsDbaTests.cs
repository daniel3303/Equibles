using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetInvestmentAdviser</c>'s "Doing business as" line. The profile
/// should surface the business name only when it actually differs from the legal name; a business
/// name that matches the legal name apart from letter casing is the same name and must be
/// suppressed, not echoed back as a redundant alias. The existing suite only seeds advisers whose
/// names differ outright, so the case-insensitive suppression branch is otherwise unexercised.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InvestmentAdviserToolsDbaTests : ParadeDbMcpTestBase
{
    public InvestmentAdviserToolsDbaTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInvestmentAdviser_BusinessNameMatchesLegalNameIgnoringCase_OmitsDoingBusinessAs()
    {
        DbContext
            .Set<FormAdvAdviser>()
            .Add(
                new FormAdvAdviser
                {
                    Crd = 777,
                    LegalName = "ACME CAPITAL ADVISORS",
                    // Same name, lower-cased — a casing-only difference is not a real alias.
                    PrimaryBusinessName = "acme capital advisors",
                    MainOfficeState = "NY",
                    TotalRegulatoryAum = 1_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var tools = new InvestmentAdviserTools(
            new FormAdvAdviserRepository(DbContext),
            errorManager: null,
            NullLogger<InvestmentAdviserTools>()
        );

        var result = await tools.GetInvestmentAdviser(777);

        // The legal name still renders as the heading; the casing-only "alias" must not appear.
        result.Should().Contain("ACME CAPITAL ADVISORS");
        result.Should().NotContain("Doing business as");
    }
}
