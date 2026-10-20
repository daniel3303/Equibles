using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class HolderProfileBuilderTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateOnly Roster = new(2026, 10, 1);
    private static readonly HolderEvidenceContext Context = new(Today, Roster, Roster);

    [Fact]
    public void CoverCrd_LinksTheHoldersOwnFormAdvListing()
    {
        var sources = Sources(advisers: [Adviser(160108)]);

        var facts = HolderProfileBuilder.CollectEvidence(Holder(crd: "000160108"), sources);

        var fact = facts.Should().ContainSingle().Subject;
        fact.Source.Should().Be(HolderEvidenceSource.FormAdvRoster);
        fact.MatchedOn.Should().Be(HolderEvidenceMatch.CoverCrd);
        fact.Field.Should().Be(HolderEvidenceFields.AdvRegistration);
        fact.Value.Should().Be(HolderEvidenceFields.AdvRegistered);
        fact.SourceKey.Should().Be("160108");
        fact.AsOf.Should().Be(Roster);
    }

    [Fact]
    public void WithoutACoverCrd_TheSecFileNumberThenTheCikLinkAListing()
    {
        var sources = Sources(
            advisers:
            [
                Adviser(1, secNumber: "801-117266"),
                Adviser(2, cik: "102909", registration: FormAdvRegistration.ExemptReporting),
            ]
        );

        var byFileNumber = HolderProfileBuilder.CollectEvidence(
            Holder(secFileNumber: "801-0117266"),
            sources
        );
        var byCik = HolderProfileBuilder.CollectEvidence(Holder(cik: "0000102909"), sources);

        byFileNumber
            .Should()
            .ContainSingle()
            .Which.MatchedOn.Should()
            .Be(HolderEvidenceMatch.CoverSecFileNumber);
        var cikFact = byCik.Should().ContainSingle().Subject;
        cikFact.MatchedOn.Should().Be(HolderEvidenceMatch.Cik);
        cikFact.Value.Should().Be(HolderEvidenceFields.AdvExemptReporting);
    }

    [Fact]
    public void ListingLinkedSeveralWays_IsOneFactUnderTheStrongestLink()
    {
        var sources = Sources(advisers: [Adviser(160108, cik: "102909", secNumber: "801-11953")]);

        var facts = HolderProfileBuilder.CollectEvidence(
            Holder(cik: "102909", crd: "160108", secFileNumber: "801-11953"),
            sources
        );

        facts.Should().ContainSingle().Which.MatchedOn.Should().Be(HolderEvidenceMatch.CoverCrd);
    }

    [Fact]
    public void CikOrFileNumberCarriedBySeveralListings_LinksNone()
    {
        var sources = Sources(
            advisers:
            [
                Adviser(1, cik: "102909", secNumber: "801-1"),
                Adviser(2, cik: "102909", secNumber: "801-1"),
            ]
        );

        HolderProfileBuilder
            .CollectEvidence(Holder(cik: "102909", secFileNumber: "801-1"), sources)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ThirteenFFileNumber_NeverLinksAnAdviser()
    {
        var sources = Sources(advisers: [Adviser(1, secNumber: "801-12345")]);

        HolderProfileBuilder
            .CollectEvidence(Holder(secFileNumber: "028-12345"), sources)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void ThirteenD_SpeaksForEachPersonByItsOwnCik()
    {
        var sources = Sources(
            beneficialOwnership:
            [
                Filing(
                    FilingType.Schedule13D,
                    filerCik: "999",
                    (
                        "0000070858",
                        ReportingPersonTypes.Bank | ReportingPersonTypes.ParentHoldingCompany
                    ),
                    ("1100", ReportingPersonTypes.BrokerDealer)
                ),
            ]
        );

        var facts = HolderProfileBuilder.CollectEvidence(Holder(cik: "70858"), sources);

        var fact = facts.Should().ContainSingle().Subject;
        fact.Source.Should().Be(HolderEvidenceSource.Schedule13D);
        fact.Field.Should().Be(HolderEvidenceFields.TypeOfReportingPerson);
        Enum.Parse<ReportingPersonTypes>(fact.Value)
            .Should()
            .Be(ReportingPersonTypes.Bank | ReportingPersonTypes.ParentHoldingCompany);
        fact.SourceKey.Should().Be("0001-26-000001");
        HolderProfileBuilder.CollectEvidence(Holder(cik: "999"), sources).Should().BeEmpty();
    }

    [Fact]
    public void ThirteenGNamingOnePerson_SpeaksForItsFiler()
    {
        var single = Filing(
            FilingType.Schedule13G,
            filerCik: "0000093751",
            (null, ReportingPersonTypes.InvestmentAdviser)
        ) with
        {
            FilingPersonTypes = ReportingPersonTypes.InvestmentAdviser,
        };
        var sources = Sources(beneficialOwnership: [single]);

        var facts = HolderProfileBuilder.CollectEvidence(Holder(cik: "93751"), sources);

        facts
            .Select(fact => fact.Field)
            .Should()
            .BeEquivalentTo([
                HolderEvidenceFields.TypeOfReportingPerson,
                HolderEvidenceFields.TypeOfPersonFiling,
            ]);
        facts.Should().OnlyContain(fact => fact.Source == HolderEvidenceSource.Schedule13G);
    }

    [Fact]
    public void ThirteenGNamingSeveralPersons_SpeaksForNobody()
    {
        // A 13G person has no CIK, so with several persons and no single Item 3 category under
        // Rule 13d-1(b) alone, the filer's own type is unknown.
        var sources = Sources(
            beneficialOwnership:
            [
                Filing(
                    FilingType.Schedule13G,
                    filerCik: "93751",
                    (null, ReportingPersonTypes.ParentHoldingCompany),
                    (null, ReportingPersonTypes.Bank)
                ),
            ]
        );

        HolderProfileBuilder.CollectEvidence(Holder(cik: "93751"), sources).Should().BeEmpty();
    }

    [Fact]
    public void SicAndRegistrant_AreReadByCanonicalCik()
    {
        var sources = Sources(
            filerProfiles:
            [
                new SecFilerProfile
                {
                    Cik = "0000019617",
                    Found = true,
                    Sic = " 6021 ",
                    FetchedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            ],
            registrants:
            [
                new NCenRegistrantRow(
                    "19617",
                    "old",
                    new DateOnly(2025, 6, 30),
                    "N-2",
                    new DateOnly(2025, 9, 1)
                ),
                // An amendment for the same period wins by its later filing date.
                new NCenRegistrantRow(
                    "19617",
                    "z-original",
                    new DateOnly(2026, 6, 30),
                    "N-2",
                    new DateOnly(2026, 9, 1)
                ),
                new NCenRegistrantRow(
                    "19617",
                    "new",
                    new DateOnly(2026, 6, 30),
                    "N-1A",
                    new DateOnly(2026, 9, 20)
                ),
            ]
        );
        var holder = Holder(cik: "19617");

        var facts = HolderProfileBuilder.CollectEvidence(holder, sources);

        facts
            .Should()
            .HaveCount(2)
            .And.OnlyContain(fact => fact.InstitutionalHolderId == holder.Id);
        var sic = facts.Single(fact => fact.Source == HolderEvidenceSource.SecFilerProfile);
        sic.Value.Should().Be("6021");
        sic.AsOf.Should().Be(new DateOnly(2026, 9, 1));
        var registrant = facts.Single(fact => fact.Source == HolderEvidenceSource.NCenRegistrant);
        registrant.SourceKey.Should().Be("new");
        registrant.Value.Should().Be("N-1A");
    }

    [Fact]
    public void Build_TakesAdviserFactsFromTheCurrentListing()
    {
        var adviser = Adviser(160108);
        adviser.TotalRegulatoryAum = 1_000;
        adviser.Lei = "5493002AVFA6DB2KHH58";
        adviser.AdvisesPrivateFunds = true;
        adviser.PrivateFundCount = 4;
        adviser.HedgeFundCount = 3;
        adviser.PrivateEquityFundCount = 1;
        adviser.PrivateFundGrossAssets = 500;
        var sources = Sources(
            advisers: [adviser],
            clientTypes:
            [
                ClientType(160108, FormAdvClientType.PooledInvestmentVehicles, 700),
                ClientType(160108, FormAdvClientType.Individuals, 300),
                // A row from an earlier roster no longer describes the adviser.
                ClientType(160108, FormAdvClientType.Corporations, 9_000, Roster.AddMonths(-1)),
            ]
        );
        var holder = Holder(crd: "160108");

        var profile = Build(holder, sources);

        profile.InstitutionalHolderId.Should().Be(holder.Id);
        profile.EntityType.Should().Be(HolderEntityType.RegisteredAdviser);
        profile.DerivationVersion.Should().Be(HolderEntityTypeDeriver.Version);
        profile.AdviserCrd.Should().Be(160108);
        profile.AdviserAsOf.Should().Be(Roster);
        profile.RegulatoryAum.Should().Be(1_000);
        profile.Lei.Should().Be("5493002AVFA6DB2KHH58");
        profile.AdvisesPrivateFunds.Should().BeTrue();
        profile.PrivateFundCount.Should().Be(4);
        profile.PrivateFundFocus.Should().Be(PrivateFundFocus.Hedge);
        profile.PrivateFundGrossAssets.Should().Be(500);
        profile.ClientBase.Should().Be(HolderClientBase.PooledInvestmentVehicles);
        profile.ClientBaseAumShare.Should().Be(0.7m);
        profile.PooledVehicleAumShare.Should().Be(0.7m);
    }

    [Fact]
    public void Build_RecentScheduleD_DecidesTheFocusByGrossAssetsWithItsFilingDate()
    {
        // The roster counts mostly hedge funds, but the filed gross assets sit in private equity.
        var adviser = Adviser(160108);
        adviser.AdvisesPrivateFunds = true;
        adviser.PrivateFundCount = 4;
        adviser.HedgeFundCount = 3;
        adviser.PrivateEquityFundCount = 1;
        var filed = Today.AddMonths(-6);
        var scheduleD = new FormAdvPrivateFundFiling
        {
            Crd = 160108,
            FiledOn = filed,
            FundCount = 4,
            GrossAssets = 1_000,
            HedgeFundCount = 3,
            HedgeFundGrossAssets = 100,
            PrivateEquityFundCount = 1,
            PrivateEquityFundGrossAssets = 900,
        };
        var older = new FormAdvPrivateFundFiling
        {
            Crd = 160108,
            FiledOn = filed.AddYears(-1),
            FundCount = 1,
            GrossAssets = 10,
            HedgeFundCount = 1,
            HedgeFundGrossAssets = 10,
        };

        var profile = Build(
            Holder(crd: "160108"),
            HolderProfileSources.Build([adviser], [], [], [], [], [], [older, scheduleD])
        );
        var rosterOnly = Build(Holder(crd: "160108"), Sources(advisers: [adviser]));

        profile.PrivateFundFocus.Should().Be(PrivateFundFocus.PrivateEquity);
        profile.PrivateFundFocusBasis.Should().Be(PrivateFundFocusBasis.GrossAssets);
        profile.PrivateFundFocusAsOf.Should().Be(filed);
        rosterOnly.PrivateFundFocus.Should().Be(PrivateFundFocus.Hedge);
        rosterOnly.PrivateFundFocusBasis.Should().Be(PrivateFundFocusBasis.FundCount);
        rosterOnly.PrivateFundFocusAsOf.Should().Be(Roster);
    }

    [Fact]
    public void Build_FormerListing_FillsNoAdviserFacts()
    {
        var adviser = Adviser(160108);
        adviser.ReportDate = Roster.AddMonths(-2);
        adviser.TotalRegulatoryAum = 1_000;

        var profile = Build(Holder(crd: "160108"), Sources(advisers: [adviser]));

        profile.EntityType.Should().Be(HolderEntityType.Unknown);
        profile.AdviserCrd.Should().BeNull();
        profile.RegulatoryAum.Should().BeNull();
        profile.ClientBase.Should().Be(HolderClientBase.Unknown);
    }

    [Fact]
    public void Build_ReadsAdvisedSeriesByTheCoverCrdWithoutAFormAdvListing()
    {
        var sources = Sources(
            advisedSeries:
            [
                new AdvisedSeriesRow(
                    160108,
                    "S1",
                    new DateOnly(2026, 6, 30),
                    NCenFundTypes.IndexFund,
                    80m
                ),
                new AdvisedSeriesRow(
                    160108,
                    "S2",
                    new DateOnly(2026, 3, 31),
                    NCenFundTypes.None,
                    20m
                ),
            ]
        );

        var profile = Build(Holder(crd: "160108"), sources);

        profile.AdvisedSeriesCount.Should().Be(2);
        profile.IndexSeriesCount.Should().Be(1);
        profile.AdvisedNetAssets.Should().Be(100m);
        profile.IndexNetAssets.Should().Be(80m);
        profile.AdvisedSeriesAsOf.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public void Build_KeepsTheFilersSic()
    {
        var sources = Sources(
            filerProfiles:
            [
                new SecFilerProfile
                {
                    Cik = "19617",
                    Found = true,
                    Sic = "6211",
                },
            ]
        );

        var profile = Build(Holder(cik: "19617"), sources);

        profile.Sic.Should().Be("6211");
        profile.EntityType.Should().Be(HolderEntityType.Unknown);
    }

    [Fact]
    public void BulkCycle_RefreshesProfilesAfterTheFilerProfiles()
    {
        // Profiles read SIC codes, so they run after the filer profiles that store them.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Equibles.sln")))
            directory = directory.Parent;
        var source = File.ReadAllText(
                Path.Combine(
                    directory.FullName,
                    "src",
                    "Equibles.Holdings.HostedService",
                    "HoldingsScraperWorker.cs"
                )
            )
            .Replace("\r\n", "\n");
        var doWork = source[source.IndexOf("protected override async Task DoWork(")..];
        doWork = doWork[..doWork.IndexOf("\n    }\n")];

        var filerProfiles = doWork.IndexOf("await RefreshHolderFilerProfiles(stoppingToken);");
        var profiles = doWork.IndexOf("await RefreshHolderProfiles(stoppingToken);");
        filerProfiles.Should().BeGreaterThan(0);
        profiles.Should().BeGreaterThan(filerProfiles);
    }

    private static InstitutionalHolderProfile Build(
        HolderIdentityRow holder,
        HolderProfileSources sources
    ) =>
        HolderProfileBuilder.Build(
            holder,
            HolderProfileBuilder.CollectEvidence(holder, sources),
            sources,
            Context
        );

    private static HolderIdentityRow Holder(
        string cik = "1",
        string crd = null,
        string secFileNumber = null
    ) => new(Guid.NewGuid(), cik, crd, secFileNumber);

    private static FormAdvAdviser Adviser(
        int crd,
        string cik = null,
        string secNumber = null,
        FormAdvRegistration registration = FormAdvRegistration.Registered
    ) =>
        new()
        {
            Crd = crd,
            Cik = cik,
            SecNumber = secNumber,
            Registration = registration,
            ReportDate = Roster,
        };

    private static FormAdvAdviserClientType ClientType(
        int crd,
        FormAdvClientType type,
        long? aum,
        DateOnly? reportDate = null
    ) =>
        new()
        {
            Crd = crd,
            ClientType = type,
            RegulatoryAum = aum,
            ReportDate = reportDate ?? Roster,
        };

    private static BeneficialOwnershipEvidenceRow Filing(
        FilingType type,
        string filerCik,
        params (string Cik, ReportingPersonTypes Types)[] persons
    ) =>
        new(
            "0001-26-000001",
            type,
            new DateOnly(2026, 9, 1),
            filerCik,
            ReportingPersonTypes.None,
            persons
        );

    private static HolderProfileSources Sources(
        IEnumerable<FormAdvAdviser> advisers = null,
        IEnumerable<FormAdvAdviserClientType> clientTypes = null,
        IEnumerable<SecFilerProfile> filerProfiles = null,
        IEnumerable<BeneficialOwnershipEvidenceRow> beneficialOwnership = null,
        IEnumerable<NCenRegistrantRow> registrants = null,
        IEnumerable<AdvisedSeriesRow> advisedSeries = null
    ) =>
        HolderProfileSources.Build(
            advisers ?? [],
            clientTypes ?? [],
            filerProfiles ?? [],
            beneficialOwnership ?? [],
            registrants ?? [],
            advisedSeries ?? []
        );
}
