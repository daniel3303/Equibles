using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for <c>GetInvestmentAdviser</c>'s fee-structure rendering when an adviser
/// declares none of the Form ADV compensation flags. The tool promises to report "how the firm
/// is compensated"; a firm with no fee flags has nothing to report, so the profile must render a
/// placeholder rather than a blank or missing value. The existing suite only ever seeds advisers
/// that charge at least one fee, so the empty-list branch (<c>fees.Count == 0</c>) is otherwise
/// unexercised.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InvestmentAdviserToolsFeeStructureTests : ParadeDbMcpTestBase
{
    public InvestmentAdviserToolsFeeStructureTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInvestmentAdviser_AdviserChargesNoFees_RendersFeeStructurePlaceholder()
    {
        DbContext
            .Set<FormAdvAdviser>()
            .Add(
                new FormAdvAdviser
                {
                    Crd = 555,
                    LegalName = "NO FEE ADVISERS LLC",
                    MainOfficeState = "NY",
                    TotalRegulatoryAum = 5_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                    // Every Charges* flag left at its default false — the firm reports no fees.
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var tools = new InvestmentAdviserTools(
            new FormAdvAdviserRepository(DbContext),
            errorManager: null,
            NullLogger<InvestmentAdviserTools>()
        );

        var result = await tools.GetInvestmentAdviser(555);

        // No compensation flags set → the fee structure must degrade to the "-" placeholder,
        // never an empty value after the label.
        result.Should().Contain("**Fee structure:** -");
    }
}
