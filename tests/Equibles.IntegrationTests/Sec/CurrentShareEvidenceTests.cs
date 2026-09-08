using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class CurrentShareEvidenceTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(2025)]
    [InlineData(2027)]
    public async Task InvalidCurrentContextFallsBackWithoutLosingForeignForm(int invalidYear)
    {
        var stock = new CommonStock
        {
            Ticker = "DATE",
            Name = "Date evidence",
            Cik = "0000001234",
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
            Label = "Shares",
        };
        FinancialFact Fact(
            decimal value,
            DateOnly asOf,
            DateOnly filed,
            DateOnly report,
            DocumentType form
        ) =>
            new()
            {
                CommonStock = stock,
                FinancialConcept = concept,
                Document = new Document
                {
                    CommonStock = stock,
                    DocumentType = form,
                    ReportingDate = filed,
                    ReportingForDate = report,
                    AccessionNumber = filed.ToString("yyyyMMdd"),
                    Content = new File
                    {
                        Name = "filing",
                        Extension = "htm",
                        ContentType = "text/html",
                    },
                },
                Value = value,
                Unit = "shares",
                PeriodType = FactPeriodType.Instant,
                PeriodStart = asOf,
                PeriodEnd = asOf,
                FiledDate = filed,
                Form = form,
                AccessionNumber = filed.ToString("yyyyMMdd"),
                DimensionsKey = "",
            };
        var rejected = Fact(
            200,
            new(invalidYear, 4, 1),
            new(2026, 5, 1),
            new(2026, 3, 31),
            DocumentType.TwentyF
        );
        DbContext.AddRange(
            Fact(100, new(2026, 1, 1), new(2026, 2, 1), new(2026, 1, 1), DocumentType.SixK),
            rejected
        );
        await DbContext.SaveChangesAsync();
        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new StockSplitRepository(DbContext)
        );
        Assert.Equal(100, await provider.GetReportedSharesOutstanding(stock));
        Assert.True(await provider.IsForeignPrivateIssuer(stock));
        Assert.Equal(new DateOnly(invalidYear, 4, 1), rejected.PeriodEnd);
        Assert.Equal(200, rejected.Value);
    }
}
