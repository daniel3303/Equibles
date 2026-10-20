using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Record-replay against real pre-XML Schedule 13D and 13G complete submissions, as EDGAR serves
// them at {accession}.txt: plain-text and HTML schedules, single and multi-person cover pages,
// and layouts the reader must refuse rather than complete.
public class Filing13DGTextParserTests
{
    private readonly Filing13DGTextParser _sut = new();

    private Schedule13DGTextParse Parse(string asset, string accession, string form) =>
        _sut.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", asset)
            ),
            accession,
            new DateOnly(2024, 12, 1),
            form
        );

    [Fact]
    public void Parse_BlackRockNvidiaPlainText13GA_ReadsEveryCoverAnswer()
    {
        var parse = Parse("sc13ga-text-blackrock-nvidia.txt", "0001086364-24-004712", "SC 13G/A");

        parse.RefusalReason.Should().BeNull();
        var filing = parse.Filing;
        filing.SubmissionType.Should().Be("SC 13G/A");
        filing.FilingType.Should().Be(FilingType.Schedule13G);
        filing.IsAmendment.Should().BeTrue();
        filing.FilerCik.Should().Be("1364742");
        filing.IssuerCik.Should().Be("1045810");
        filing.IssuerName.Should().Be("NVIDIA CORP");
        filing.IssuerCusip.Should().Be("67066G104");
        filing.DateOfEvent.Should().Be(new DateOnly(2023, 12, 31));
        filing.SecuritiesClassTitle.Should().Be("Common Stock");
        filing.RuleDesignations.Should().Equal("Rule 13d-1(b)");
        filing.TextParserVersion.Should().Be(Filing13DGTextParser.Version);

        var person = filing.ReportingPersons.Should().ContainSingle().Subject;
        person.Name.Should().Be("BlackRock, Inc.");
        // A lone reporting person is the filer, whose CIK the header carries.
        person.Cik.Should().Be("1364742");
        person.SoleVotingPower.Should().Be(162_856_513);
        person.SharedVotingPower.Should().Be(0);
        person.SoleDispositivePower.Should().Be(180_593_555);
        person.SharedDispositivePower.Should().Be(0);
        person.AggregateAmountOwned.Should().Be(180_593_555);
        person.PercentOfClass.Should().Be(7.3m);
        person.TypeCodes.Should().Equal("HC");
        person.CitizenshipOrOrganization.Should().Be("Delaware");
    }

    [Fact]
    public void Parse_VanguardNvidiaHtml13GA_ReadsCaptionsWithAnswersAfterThem()
    {
        var filing = Parse(
            "sc13ga-html-vanguard-nvidia.txt",
            "0001104659-24-021596",
            "SC 13G/A"
        ).Filing;

        filing.FilerCik.Should().Be("102909");
        filing.IssuerCusip.Should().Be("67066G104");
        filing.DateOfEvent.Should().Be(new DateOnly(2023, 12, 29));
        filing.RuleDesignations.Should().Equal("Rule 13d-1(b)");
        var person = filing.ReportingPersons.Should().ContainSingle().Subject;
        // The filer appends its tax number to the name; it is not part of the name.
        person.Name.Should().Be("The Vanguard Group");
        person.SharedVotingPower.Should().Be(3_257_646);
        person.SoleDispositivePower.Should().Be(193_993_095);
        person.SharedDispositivePower.Should().Be(10_511_843);
        person.AggregateAmountOwned.Should().Be(204_504_938);
        person.PercentOfClass.Should().Be(8.28m);
        person.TypeCodes.Should().Equal("IA");
    }

    [Fact]
    public void Parse_NvidiaFilingOnServeRobotics_TakesTheSubjectCompanyFromTheHeaderNotTheFiler()
    {
        var filing = Parse("sc13g-html-nvidia-serve.txt", "0001045810-24-000230", "SC 13G").Filing;

        filing.FilerCik.Should().Be("1045810");
        filing.IssuerCik.Should().Be("1832483");
        filing.IssuerName.Should().Be("Serve Robotics Inc. /DE/");
        // The CUSIP is filed with spaces between its groups.
        filing.IssuerCusip.Should().Be("81758H106");
        filing.DateOfEvent.Should().Be(new DateOnly(2023, 7, 31));
        // A column of boxes beside a column of captions.
        filing.RuleDesignations.Should().Equal("Rule 13d-1(c)");
        var person = filing.ReportingPersons.Should().ContainSingle().Subject;
        person.Name.Should().Be("NVIDIA Corporation");
        person.Cik.Should().Be("1045810");
        person.AggregateAmountOwned.Should().Be(3_727_033);
        person.PercentOfClass.Should().Be(10.0m);
        person.TypeCodes.Should().Equal("CO");
    }

    [Fact]
    public void Parse_MultiPerson13GA_LeavesPersonCiksUnsetBecauseCoverPagesCarryNone()
    {
        var filing = Parse(
            "sc13ga-text-morganstanley-two-column.txt",
            "0000895421-24-000082",
            "SC 13G/A"
        ).Filing;

        filing.FilerCik.Should().Be("895421");
        filing.IssuerCusip.Should().Be("855668109");
        filing
            .ReportingPersons.Select(person =>
                (
                    person.Name,
                    person.Cik,
                    person.SharedDispositivePower,
                    person.AggregateAmountOwned,
                    person.PercentOfClass,
                    string.Join(",", person.TypeCodes)
                )
            )
            .Should()
            .Equal(
                ("Morgan Stanley", null, 394_385L, 394_385L, 5.7m, "HC,CO"),
                ("Morgan Stanley Smith Barney LLC", null, 394_385L, 394_385L, 5.7m, "BD,IA")
            );
        // A two-column text cover interleaves the side heading with the answers.
        filing.ReportingPersons.Should().OnlyContain(person => person.SoleVotingPower == 0);
        filing
            .ReportingPersons.Should()
            .OnlyContain(person => person.CitizenshipOrOrganization == "Delaware");
    }

    [Fact]
    public void Parse_13DAmendment_ReadsAnswersWithGluedFootnoteMarkers()
    {
        var filing = Parse(
            "sc13da-html-applied-therapeutics.txt",
            "0001104659-24-089780",
            "SC 13D/A"
        ).Filing;

        filing.FilingType.Should().Be(FilingType.Schedule13D);
        filing.IsAmendment.Should().BeTrue();
        filing.IssuerCik.Should().Be("1697532");
        filing.IssuerCusip.Should().Be("03828A101");
        filing.DateOfEvent.Should().Be(new DateOnly(2024, 8, 12));
        filing.RuleDesignations.Should().BeEmpty();
        var person = filing.ReportingPersons.Should().ContainSingle().Subject;
        person.Name.Should().Be("Shoshana Shendelman");
        person.SoleVotingPower.Should().Be(6_020_981);
        person.SharedVotingPower.Should().Be(1_782_374);
        person.AggregateAmountOwned.Should().Be(7_803_355);
        person.PercentOfClass.Should().Be(6.6m);
        person.TypeCodes.Should().Equal("IN");
    }

    [Fact]
    public void Parse_Original13DWithTwoPersons_ReadsEachCoverPage()
    {
        var filing = Parse(
            "sc13d-html-american-heritage.txt",
            "0001214659-24-015452",
            "SC 13D"
        ).Filing;

        filing.IsAmendment.Should().BeFalse();
        filing.IssuerCusip.Should().Be("77467X101");
        filing.DateOfEvent.Should().Be(new DateOnly(2024, 8, 5));
        filing
            .ReportingPersons.Select(person =>
                (
                    person.Name,
                    person.SoleVotingPower,
                    person.AggregateAmountOwned,
                    person.PercentOfClass
                )
            )
            .Should()
            .Equal(
                ("American Heritage Railways, Inc.", 0L, 1_000_000L, 13.2m),
                ("Allen C. Harper", 1_911L, 1_001_911L, 13.2m)
            );
    }

    [Fact]
    public void Parse_13DWithoutACusip_LeavesItUnsetForTheSubjectCompanyToResolve()
    {
        var filing = Parse("sc13d-text-bulldog.txt", "0001398344-24-000853", "SC 13D").Filing;

        filing.IssuerCusip.Should().BeNull();
        filing.IssuerCik.Should().Be("1811074");
        // The event date follows its caption, and the percent caption omits "amount in".
        filing.DateOfEvent.Should().Be(new DateOnly(2024, 1, 17));
        filing
            .ReportingPersons.Select(person => person.Name)
            .Should()
            .Equal("Bulldog Investors, LLP", "Phillip Goldstein", "Andrew Dakos");
        filing
            .ReportingPersons.Should()
            .OnlyContain(person => person.AggregateAmountOwned == 4_502);
        filing.ReportingPersons.Should().OnlyContain(person => person.PercentOfClass == 0.06m);
    }

    [Fact]
    public void Parse_SedolInsteadOfCusip_LeavesTheCusipUnset()
    {
        var filing = Parse(
            "sc13ga-text-blackrock-sedol.txt",
            "0001086364-24-003191",
            "SC 13G/A"
        ).Filing;

        filing.IssuerCusip.Should().BeNull();
        filing.IssuerCik.Should().Be("351834");
        filing
            .ReportingPersons.Should()
            .ContainSingle()
            .Which.AggregateAmountOwned.Should()
            .Be(9_984_820);
    }

    [Fact]
    public void Parse_SuperscriptFootnotes_StayApartFromTheFigures()
    {
        var person = Parse("sc13g-html-sio-superscripts.txt", "0001214659-24-002595", "SC 13G")
            .Filing.ReportingPersons.Should()
            .ContainSingle()
            .Subject;

        // Filed as "280,000<sup>1</sup>"; read as one string it would be 2,800,001.
        person.SharedVotingPower.Should().Be(280_000);
        person.SharedDispositivePower.Should().Be(280_000);
        person.AggregateAmountOwned.Should().Be(280_000);
        person.PercentOfClass.Should().Be(7.49m);
    }

    [Fact]
    public void Parse_SymbolFontCheckboxes_ReadsTheCheckedRule()
    {
        var filing = Parse("sc13g-html-jpmorgan-lyft.txt", "0000019617-24-000603", "SC 13G").Filing;

        filing.RuleDesignations.Should().Equal("Rule 13d-1(b)");
        filing.IssuerCusip.Should().Be("55087P104");
        filing.DateOfEvent.Should().Be(new DateOnly(2024, 9, 30));
        var person = filing.ReportingPersons.Should().ContainSingle().Subject;
        person.Name.Should().Be("JPMORGAN CHASE & CO.");
        person.AggregateAmountOwned.Should().Be(26_190_527);
        person.PercentOfClass.Should().Be(6.5m);
    }

    [Fact]
    public void Parse_VariantCaptions_StillReadTheRows()
    {
        var filing = Parse(
            "sc13ga-text-statestreet.txt",
            "0000093751-24-000227",
            "SC 13G/A"
        ).Filing;

        // "AGGREGATED AMOUNT ..." and answers suffixed "SHARES".
        var person = filing.ReportingPersons.Should().ContainSingle().Subject;
        person.SharedVotingPower.Should().Be(954_216);
        person.SharedDispositivePower.Should().Be(998_042);
        person.AggregateAmountOwned.Should().Be(998_042);
        person.PercentOfClass.Should().Be(6.23m);
    }

    [Fact]
    public void Parse_SeeItemReferenceBeforeTheAnswer_ReadsTheAnswer()
    {
        var filing = Parse(
            "sc13d-html-franklin-see-item.txt",
            "0001104659-24-023494",
            "SC 13D"
        ).Filing;

        filing.ReportingPersons.Should().HaveCount(4);
        filing.ReportingPersons.Should().OnlyContain(person => person.SharedVotingPower == 40);
        filing.ReportingPersons.Should().OnlyContain(person => person.AggregateAmountOwned == 40);
        filing.ReportingPersons.Should().OnlyContain(person => person.PercentOfClass == 100m);
    }

    [Fact]
    public void Parse_CoverPagesWithRowNumbersOnly_IsRefused()
    {
        var parse = Parse(
            "sc13ga-text-fmr-nvidia-row-numbers.txt",
            "0000315066-24-001331",
            "SC 13G/A"
        );

        // FMR answers "Item 5: 121,060,256" without captions and states no event date.
        parse.IsRefused.Should().BeTrue();
        parse.Filing.ReportingPersons.Should().BeEmpty();
        // The header facts still type and attribute the parked row.
        parse.Filing.FilingType.Should().Be(FilingType.Schedule13G);
        parse.Filing.FilerCik.Should().Be("315066");
        parse.Filing.IssuerCik.Should().Be("1045810");
    }

    [Fact]
    public void Parse_PowerRowsSplitAcrossEntities_IsRefused()
    {
        var parse = Parse(
            "sc13ga-html-firsttrust-per-entity.txt",
            "0001445546-24-006226",
            "SC 13G/A"
        );

        parse.RefusalReason.Should().Be("cover page 1: unreadable shared voting power");
        parse.Filing.ReportingPersons.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SubmissionWithoutHeader_IsRefusedAndTypedByTheIndexForm()
    {
        var parse = _sut.Parse("not a submission", "0000000001-24-000001", default, "SC 13D/A");

        parse.RefusalReason.Should().Be("no submission header");
        parse.Filing.FilingType.Should().Be(FilingType.Schedule13D);
        parse.Filing.IsAmendment.Should().BeTrue();
    }

    [Fact]
    public void Parse_HeaderWithTwoFilers_IsRefused()
    {
        const string submission = """
            <SEC-DOCUMENT>0000000001-24-000001.txt : 20240101
            <SEC-HEADER>0000000001-24-000001.hdr.sgml : 20240101
            ACCESSION NUMBER:		0000000001-24-000001
            CONFORMED SUBMISSION TYPE:	SC 13G
            SUBJECT COMPANY:
            	COMPANY DATA:
            		COMPANY CONFORMED NAME:			ISSUER
            		CENTRAL INDEX KEY:			0000000010
            FILED BY:
            	COMPANY DATA:
            		COMPANY CONFORMED NAME:			FIRST
            		CENTRAL INDEX KEY:			0000000020
            FILED BY:
            	COMPANY DATA:
            		COMPANY CONFORMED NAME:			SECOND
            		CENTRAL INDEX KEY:			0000000030
            </SEC-HEADER>
            """;

        var parse = _sut.Parse(submission, "0000000001-24-000001", default, "SC 13G");

        parse.RefusalReason.Should().Be("2 filers");
        parse.Filing.IssuerCik.Should().BeNull();
    }
}
