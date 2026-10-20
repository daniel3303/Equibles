using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderIdentifierRulesTests
{
    private static readonly HolderEvidenceContext Context = new(
        new DateOnly(2026, 10, 20),
        new DateOnly(2026, 10, 1),
        new DateOnly(2026, 10, 1)
    );

    [Fact]
    public void MultiPerson13G_SingleItem3Code_13d1bOnly()
    {
        // FMR LLC's joint 13G with Abigail P. Johnson (HC and IN on the cover pages), filed under
        // Rule 13d-1(b) alone, whose Item 3 checks HC.
        var filing = Recorded(
            "sc13g-fmr-joint-hc.xml",
            "0000315066-26-002332",
            "0000315066",
            new DateOnly(2026, 10, 7)
        );

        filing.Persons.Should().HaveCount(2);
        filing.Rules.Should().Be(Schedule13GRules.QualifiedInstitution);
        var (cik, fact) = HolderIdentifierRules
            .AttributeBeneficialOwnership(filing)
            .Should()
            .ContainSingle()
            .Subject;
        cik.Should().Be("315066");
        fact.Source.Should().Be(HolderEvidenceSource.Schedule13G);
        fact.Field.Should().Be(HolderEvidenceFields.TypeOfPersonFiling);
        fact.Value.Should().Be(nameof(ReportingPersonTypes.ParentHoldingCompany));
        fact.SourceKey.Should().Be("0000315066-26-002332");
        fact.AsOf.Should().Be(new DateOnly(2026, 10, 7));
        HolderEntityTypeDeriver
            .Derive([fact], Context)
            .EntityType.Should()
            .Be(HolderEntityType.HoldingCompany);
    }

    [Theory]
    [InlineData(Schedule13GRules.PassiveInvestor, ReportingPersonTypes.ParentHoldingCompany, null)]
    [InlineData(
        Schedule13GRules.QualifiedInstitution | Schedule13GRules.PassiveInvestor,
        ReportingPersonTypes.ParentHoldingCompany,
        null
    )]
    [InlineData(Schedule13GRules.None, ReportingPersonTypes.ParentHoldingCompany, null)]
    [InlineData(
        Schedule13GRules.QualifiedInstitution,
        ReportingPersonTypes.ParentHoldingCompany | ReportingPersonTypes.InvestmentAdviser,
        null
    )]
    [InlineData(Schedule13GRules.QualifiedInstitution, ReportingPersonTypes.None, null)]
    [InlineData(
        Schedule13GRules.QualifiedInstitution,
        ReportingPersonTypes.ParentHoldingCompany,
        "XX"
    )]
    public void MultiPerson13G_WithoutOneItem3CodeUnder13d1bAlone_SpeaksForNobody(
        Schedule13GRules rules,
        ReportingPersonTypes item3,
        string unrecognized
    )
    {
        var filing = Recorded(
            "sc13g-fmr-joint-hc.xml",
            "0000315066-26-002332",
            "0000315066",
            new DateOnly(2026, 10, 7)
        ) with
        {
            Rules = rules,
            FilingPersonTypes = item3,
            UnrecognizedFilingPersonTypeCodes = unrecognized,
        };

        HolderIdentifierRules.AttributeBeneficialOwnership(filing).Should().BeEmpty();
    }

    [Fact]
    public void SinglePerson13G_ParentHoldingCompany_ReadsHoldingCompany()
    {
        // BlackRock, Inc.'s 13G: one person, HC on its cover page and in Item 3.
        var filing = Recorded(
            "sc13g-blackrock-hc.xml",
            "0002012383-26-003340",
            "0002012383",
            new DateOnly(2026, 10, 5)
        );

        var attributed = HolderIdentifierRules.AttributeBeneficialOwnership(filing).ToList();

        attributed.Should().OnlyContain(item => item.Cik == "2012383");
        attributed
            .Select(item => item.Fact.Field)
            .Should()
            .Equal(
                HolderEvidenceFields.TypeOfReportingPerson,
                HolderEvidenceFields.TypeOfPersonFiling
            );
        var decision = HolderEntityTypeDeriver.Derive(
            attributed.Select(item => item.Fact),
            Context
        );
        decision.EntityType.Should().Be(HolderEntityType.HoldingCompany);
        decision.HasConflictingEvidence.Should().BeFalse();
    }

    [Theory]
    [InlineData("000160108", 160108)]
    [InlineData(" 105958 ", 105958)]
    [InlineData("0000000160108", 160108)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("0", null)]
    [InlineData("N/A", null)]
    [InlineData("12-345", null)]
    [InlineData("1234567890", null)]
    public void ParseCrd_ReadsTheFiledNumber(string filed, int? expected)
    {
        HolderIdentifierRules.ParseCrd(filed).Should().Be(expected);
    }

    [Theory]
    [InlineData("801-0117266", "801-117266")]
    [InlineData("801-117266", "801-117266")]
    [InlineData("802-01234", "802-1234")]
    [InlineData(" 0801-56 ", "801-56")]
    [InlineData("028-12345", null)]
    [InlineData("803-12345", null)]
    [InlineData("801-0", null)]
    [InlineData("801", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeSecFileNumber_KeepsOnlyAdviserFileNumbers(string filed, string expected)
    {
        HolderIdentifierRules.NormalizeSecFileNumber(filed).Should().Be(expected);
    }

    [Theory]
    [InlineData("028-04295", "28-4295")]
    [InlineData("28-4295", "28-4295")]
    [InlineData(" 0028-04295 ", "28-4295")]
    [InlineData("801-117266", null)]
    [InlineData("028-0", null)]
    [InlineData("028-04295-01", null)]
    [InlineData("S028-04295", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeForm13FFileNumber_KeepsOnlyForm13FFileNumbers(
        string filed,
        string expected
    )
    {
        HolderIdentifierRules.NormalizeForm13FFileNumber(filed).Should().Be(expected);
    }

    [Fact]
    public void SummarizeAdvisedSeries_CountsEachSeriesOnceAtItsLatestReport()
    {
        var summaries = HolderIdentifierRules.SummarizeAdvisedSeries([
            new AdvisedSeriesRow(1, "S1", new DateOnly(2025, 12, 31), NCenFundTypes.None, 50m),
            new AdvisedSeriesRow(
                1,
                "S1",
                new DateOnly(2026, 6, 30),
                NCenFundTypes.IndexFund | NCenFundTypes.ExchangeTradedFund,
                60m
            ),
            new AdvisedSeriesRow(
                1,
                "S2",
                new DateOnly(2026, 3, 31),
                NCenFundTypes.MultipleOrInverseIndexFund,
                null
            ),
            new AdvisedSeriesRow(
                1,
                "S3",
                new DateOnly(2026, 3, 31),
                NCenFundTypes.ExchangeTradedManagedFund,
                10m
            ),
            new AdvisedSeriesRow(2, "S4", new DateOnly(2026, 3, 31), NCenFundTypes.None, null),
        ]);

        summaries[1]
            .Should()
            .Be(new AdvisedSeriesSummary(3, 2, 2, 70m, 60m, new DateOnly(2026, 6, 30)));
        // No reported net assets is unknown, never zero.
        summaries[2].NetAssets.Should().BeNull();
        summaries[2].IndexNetAssets.Should().BeNull();
    }

    [Fact]
    public void SummarizeAdvisedSeries_SeriesThatChangedAdviser_CountsOnlyForTheCurrentOne()
    {
        var summaries = HolderIdentifierRules.SummarizeAdvisedSeries([
            new AdvisedSeriesRow(100, "S1", new DateOnly(2025, 9, 30), NCenFundTypes.None, 50m),
            new AdvisedSeriesRow(200, "S1", new DateOnly(2026, 9, 30), NCenFundTypes.None, 60m),
            // Co-advisers named on one report both count it.
            new AdvisedSeriesRow(200, "S2", new DateOnly(2026, 6, 30), NCenFundTypes.None, 10m),
            new AdvisedSeriesRow(300, "S2", new DateOnly(2026, 6, 30), NCenFundTypes.None, 10m),
        ]);

        summaries.Keys.Should().BeEquivalentTo([200, 300]);
        summaries[200].SeriesCount.Should().Be(2);
        summaries[200].NetAssets.Should().Be(70m);
        summaries[300].SeriesCount.Should().Be(1);
    }

    [Fact]
    public void SummarizeAdvisedSeries_LatestReportWithoutAnAdviserCrd_CountsForNobody()
    {
        var summaries = HolderIdentifierRules.SummarizeAdvisedSeries([
            new AdvisedSeriesRow(100, "S1", new DateOnly(2025, 9, 30), NCenFundTypes.None, 50m),
            new AdvisedSeriesRow(null, "S1", new DateOnly(2026, 9, 30), NCenFundTypes.None, 60m),
        ]);

        summaries.Should().BeEmpty();
    }

    // A recorded primary_doc.xml read the way the detail sync stores it and the profile refresh
    // loads it back.
    private static BeneficialOwnershipEvidenceRow Recorded(
        string fixture,
        string accessionNumber,
        string cik,
        DateOnly filingDate
    )
    {
        var xml = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", fixture)
        );
        var entity = BeneficialOwnershipDetailWriter.ToEntity(
            new Filing13DGXmlParser().ParseFiling(xml, accessionNumber, cik, filingDate)
        );
        return new BeneficialOwnershipEvidenceRow(
            entity.AccessionNumber,
            entity.FilingType,
            entity.FilingDate,
            entity.FilerCik,
            entity.FilingPersonTypes,
            entity
                .ReportingPersons.OrderBy(person => person.Ordinal)
                .Select(person => (person.Cik, person.Types))
                .ToList()
        )
        {
            Rules = entity.Rules,
            UnrecognizedFilingPersonTypeCodes = entity.UnrecognizedFilingPersonTypeCodes,
        };
    }
}
