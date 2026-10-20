using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The profile refresh types every holder from the regulatory records stored for its own
/// identifiers and replaces the whole evidence and profile set on each run.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HolderProfileRefreshServiceTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Today = new(2026, 10, 17);
    private static readonly DateOnly Roster = new(2026, 10, 1);

    public HolderProfileRefreshServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_TypesEachHolderFromItsOwnRegulatoryRecords()
    {
        var adviser = Holder("102909", crd: "000105958");
        var bank = Holder("0000019617");
        var selfReported = Holder("1336528");
        var fund = Holder("36405");
        var unknown = Holder("1067983");
        var former = Holder("2000", crd: "777");
        DbContext.AddRange(
            new FormAdvRosterImport
            {
                Registration = FormAdvRegistration.Registered,
                ReportDate = Roster,
                ParserVersion = 1,
                AdviserCount = 2,
            },
            Adviser(105958, Roster, aum: 1_000),
            Adviser(777, Roster.AddMonths(-1), aum: 5),
            ClientType(105958, FormAdvClientType.InvestmentCompanies, 800),
            ClientType(105958, FormAdvClientType.Individuals, 200),
            new SecFilerProfile
            {
                Cik = "19617",
                Found = true,
                Sic = "6021",
                FetchedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            BeneficialOwnership(
                "0001336528-26-000001",
                FilingType.Schedule13D,
                Today.AddMonths(-2),
                ("1336528", ReportingPersonTypes.InvestmentAdviser)
            ),
            // Older than the 36-month look-back, so it does not speak for the bank today.
            BeneficialOwnership(
                "0000019617-22-000001",
                FilingType.Schedule13D,
                Today.AddMonths(-40),
                ("19617", ReportingPersonTypes.BrokerDealer)
            )
        );
        AddFundReport("36405", "0000036405-26-000001", new DateOnly(2026, 6, 30), "S1", 105958);
        await DbContext.SaveChangesAsync();

        (await Sut().Run(Today, CancellationToken.None)).Should().Be(6);

        var profiles = await Profiles();
        var vanguard = profiles[adviser];
        vanguard.EntityType.Should().Be(HolderEntityType.RegisteredAdviser);
        vanguard.EntityTypeSource.Should().Be(HolderEvidenceSource.FormAdvRoster);
        vanguard.EntityTypeSourceKey.Should().Be("105958");
        vanguard.RegulatoryAum.Should().Be(1_000);
        vanguard.ClientBase.Should().Be(HolderClientBase.RegisteredFunds);
        vanguard.ClientBaseAumShare.Should().Be(0.8m);
        vanguard.AdvisedSeriesCount.Should().Be(1);
        vanguard.IndexSeriesCount.Should().Be(1);
        vanguard.AdvisedNetAssets.Should().Be(100m);
        vanguard.DerivationVersion.Should().Be(HolderEntityTypeDeriver.Version);
        profiles[bank].EntityType.Should().Be(HolderEntityType.Bank);
        profiles[bank].Sic.Should().Be("6021");
        profiles[bank].HasConflictingEvidence.Should().BeFalse();
        profiles[selfReported].EntityType.Should().Be(HolderEntityType.InvestmentAdviser);
        profiles[selfReported].EntityTypeSourceKey.Should().Be("0001336528-26-000001");
        profiles[fund].EntityType.Should().Be(HolderEntityType.RegisteredFund);
        profiles[unknown].EntityType.Should().Be(HolderEntityType.Unknown);
        profiles[former].EntityType.Should().Be(HolderEntityType.Unknown);
        profiles[former].AdviserCrd.Should().BeNull();

        await using var verify = Fixture.CreateDbContext();
        var evidence = await verify.Set<HolderClassificationEvidence>().ToListAsync();
        // The former listing stays on record as evidence; only currency keeps it from deciding.
        // The 13D older than the look-back is never loaded.
        evidence.Should().HaveCount(5);
        evidence.Where(fact => fact.InstitutionalHolderId == unknown).Should().BeEmpty();
        evidence
            .Single(fact => fact.InstitutionalHolderId == former)
            .AsOf.Should()
            .Be(Roster.AddMonths(-1));
    }

    [Fact]
    public async Task Run_PublishedRosterNotImportedYet_KeepsTheCurrentListings()
    {
        // The history walk records November as published before the current view imports it;
        // until then October's listings stay current.
        var adviser = Holder("102909", crd: "000105958");
        DbContext.AddRange(
            new FormAdvRosterImport
            {
                Registration = FormAdvRegistration.Registered,
                ReportDate = Roster,
                ParserVersion = 1,
                AdviserCount = 1,
            },
            new FormAdvRosterImport
            {
                Registration = FormAdvRegistration.Registered,
                ReportDate = Roster.AddMonths(1),
            },
            Adviser(105958, Roster, aum: 1_000)
        );
        await DbContext.SaveChangesAsync();

        await Sut().Run(Today, CancellationToken.None);

        var vanguard = (await Profiles())[adviser];
        vanguard.EntityType.Should().Be(HolderEntityType.RegisteredAdviser);
        vanguard.AdviserCrd.Should().Be(105958);
    }

    [Fact]
    public async Task Run_RecentScheduleD_DecidesThePrivateFundFocusByGrossAssets()
    {
        var adviser = Holder("102909", crd: "000105958");
        var listing = Adviser(105958, Roster, aum: 1_000);
        listing.AdvisesPrivateFunds = true;
        listing.PrivateFundCount = 3;
        listing.HedgeFundCount = 2;
        listing.RealEstateFundCount = 1;
        DbContext.AddRange(
            new FormAdvRosterImport
            {
                Registration = FormAdvRegistration.Registered,
                ReportDate = Roster,
                ParserVersion = 1,
                AdviserCount = 1,
            },
            listing,
            new FormAdvPrivateFundFiling
            {
                Crd = 105958,
                Registration = FormAdvRegistration.Registered,
                FilingId = 1,
                FiledOn = new DateOnly(2024, 12, 20),
                FundCount = 3,
                GrossAssets = 1_000,
                HedgeFundCount = 2,
                HedgeFundGrossAssets = 100,
                RealEstateFundCount = 1,
                RealEstateFundGrossAssets = 900,
            }
        );
        await DbContext.SaveChangesAsync();

        await Sut().Run(Today, CancellationToken.None);

        var vanguard = (await Profiles())[adviser];
        vanguard.PrivateFundFocus.Should().Be(PrivateFundFocus.RealEstate);
        vanguard.PrivateFundFocusBasis.Should().Be(PrivateFundFocusBasis.GrossAssets);
        vanguard.PrivateFundFocusAsOf.Should().Be(new DateOnly(2024, 12, 20));
    }

    [Fact]
    public async Task Run_JointThirteenGUnderRule13d1bWithOneItem3Code_TypesItsFiler()
    {
        var parent = Holder("315066");
        var passive = Holder("1100");
        var qualified = Joint13G("0000315066-26-002332", "315066");
        qualified.Rules = Schedule13GRules.QualifiedInstitution;
        qualified.FilingPersonTypes = ReportingPersonTypes.ParentHoldingCompany;
        // Rule 13d-1(c) filers state no Item 3 category that describes them.
        var underPassiveRule = Joint13G("0000001100-26-000001", "1100");
        underPassiveRule.Rules = Schedule13GRules.PassiveInvestor;
        underPassiveRule.FilingPersonTypes = ReportingPersonTypes.InvestmentAdviser;
        DbContext.AddRange(qualified, underPassiveRule);
        await DbContext.SaveChangesAsync();

        await Sut().Run(Today, CancellationToken.None);

        var profiles = await Profiles();
        profiles[parent].EntityType.Should().Be(HolderEntityType.HoldingCompany);
        profiles[parent].EntityTypeSource.Should().Be(HolderEvidenceSource.Schedule13G);
        profiles[parent].EntityTypeSourceKey.Should().Be("0000315066-26-002332");
        profiles[passive].EntityType.Should().Be(HolderEntityType.Unknown);
    }

    [Fact]
    public async Task Run_Again_ReplacesTheStoredGeneration()
    {
        var holder = Holder("19617");
        var filerProfile = new SecFilerProfile
        {
            Cik = "19617",
            Found = true,
            Sic = "6021",
        };
        DbContext.Add(filerProfile);
        await DbContext.SaveChangesAsync();
        await Sut().Run(Today, CancellationToken.None);

        filerProfile.Sic = "6311";
        await DbContext.SaveChangesAsync();
        await Sut().Run(Today, CancellationToken.None);

        var profile = (await Profiles()).Should().ContainSingle().Subject.Value;
        profile.EntityType.Should().Be(HolderEntityType.InsuranceCompany);
        await using (var verify = Fixture.CreateDbContext())
            (await verify.Set<HolderClassificationEvidence>().SingleAsync())
                .Value.Should()
                .Be("6311");

        DbContext.Remove(filerProfile);
        await DbContext.SaveChangesAsync();
        await Sut().Run(Today, CancellationToken.None);

        (await Profiles())[holder].EntityType.Should().Be(HolderEntityType.Unknown);
        await using (var verify = Fixture.CreateDbContext())
            (await verify.Set<HolderClassificationEvidence>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Run_CountsOnlyCurrentSeriesTheAdviserManagesAsPrimaryAdviser()
    {
        var holder = Holder("102909", crd: "105958");
        AddFundReport("1", "a-1", new DateOnly(2026, 6, 30), "S1", 105958);
        AddFundReport(
            "1",
            "a-2",
            new DateOnly(2026, 6, 30),
            "S2",
            105958,
            NCenAdviserRole.SubAdviser
        );
        AddFundReport("1", "a-3", new DateOnly(2026, 6, 30), "S3", 105958, superseded: true);
        // Older than the 21-month window: the series is no longer reported as advised.
        AddFundReport("1", "a-4", new DateOnly(2024, 12, 31), "S4", 105958);
        // A yearly report that reaches SEC's data sets late is still current at 18 months.
        AddFundReport("1", "a-7", new DateOnly(2025, 4, 30), "S5", 105958);
        // A registrant without series is one fund across its reports.
        AddFundReport("2", "a-5", new DateOnly(2025, 12, 31), null, 105958);
        AddFundReport("2", "a-6", new DateOnly(2026, 6, 30), null, 105958);
        await DbContext.SaveChangesAsync();

        await Sut().Run(Today, CancellationToken.None);

        var profile = (await Profiles())[holder];
        profile.AdvisedSeriesCount.Should().Be(3);
        profile.AdvisedNetAssets.Should().Be(300m);
        profile.AdvisedSeriesAsOf.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Fact]
    public async Task Run_MoneyMarketSeries_CountsItsDailyAverageNetAssets()
    {
        var holder = Holder("102909", crd: "105958");
        AddFundReport("1", "a-1", new DateOnly(2026, 6, 30), "S1", 105958, dailyAverage: true);
        await DbContext.SaveChangesAsync();

        await Sut().Run(Today, CancellationToken.None);

        (await Profiles())[holder].AdvisedNetAssets.Should().Be(100m);
    }

    [Fact]
    public async Task Run_WithoutARosterLedger_JudgesListingsAgainstTheNewestStoredRow()
    {
        var current = Holder("1", crd: "11");
        var former = Holder("2", crd: "22");
        var exempt = Adviser(11, Roster, aum: 0);
        exempt.Registration = FormAdvRegistration.ExemptReporting;
        var older = Adviser(22, Roster.AddMonths(-4), aum: 0);
        older.Registration = FormAdvRegistration.ExemptReporting;
        DbContext.AddRange(exempt, older);
        await DbContext.SaveChangesAsync();

        await Sut().Run(Today, CancellationToken.None);

        var profiles = await Profiles();
        profiles[current].EntityType.Should().Be(HolderEntityType.ExemptReportingAdviser);
        profiles[former].EntityType.Should().Be(HolderEntityType.Unknown);
    }

    private Guid Holder(string cik, string crd = null)
    {
        var holder = new InstitutionalHolder
        {
            Cik = cik,
            Name = "Filer " + cik,
            CrdNumber = crd,
        };
        DbContext.Add(holder);
        return holder.Id;
    }

    private static FormAdvAdviser Adviser(int crd, DateOnly reportDate, long aum) =>
        new()
        {
            Crd = crd,
            LegalName = "Adviser " + crd,
            Registration = FormAdvRegistration.Registered,
            TotalRegulatoryAum = aum,
            ReportDate = reportDate,
        };

    private static FormAdvAdviserClientType ClientType(int crd, FormAdvClientType type, long aum) =>
        new()
        {
            Crd = crd,
            ClientType = type,
            RegulatoryAum = aum,
            ReportDate = Roster,
        };

    // A 13G naming a parent and the person who controls it; 13G persons carry no CIK.
    private static BeneficialOwnershipFiling Joint13G(string accession, string filerCik)
    {
        var filing = BeneficialOwnership(
            accession,
            FilingType.Schedule13G,
            Today.AddMonths(-1),
            (null, ReportingPersonTypes.ParentHoldingCompany),
            (null, ReportingPersonTypes.Individual)
        );
        filing.FilerCik = filerCik;
        return filing;
    }

    private static BeneficialOwnershipFiling BeneficialOwnership(
        string accession,
        FilingType type,
        DateOnly filed,
        params (string Cik, ReportingPersonTypes Types)[] persons
    )
    {
        var filing = new BeneficialOwnershipFiling
        {
            AccessionNumber = accession,
            FilingType = type,
            FilingDate = filed,
            FilerCik = persons[0].Cik,
            IssuerCik = "320193",
        };
        filing.ReportingPersons.AddRange(
            persons.Select(
                (person, ordinal) =>
                    new BeneficialOwnershipReportingPerson
                    {
                        BeneficialOwnershipFilingId = filing.Id,
                        Ordinal = ordinal,
                        Cik = person.Cik,
                        Name = "Person " + ordinal,
                        Types = person.Types,
                        IsLead = ordinal == 0,
                    }
            )
        );
        return filing;
    }

    private void AddFundReport(
        string registrantCik,
        string accession,
        DateOnly period,
        string seriesId,
        int crd,
        NCenAdviserRole role = NCenAdviserRole.InvestmentAdviser,
        bool superseded = false,
        bool dailyAverage = false
    )
    {
        var report = new NCenRegistrantReport
        {
            AccessionNumber = accession,
            RegistrantCik = registrantCik,
            RegistrantName = "Registrant " + registrantCik,
            InvestmentCompanyType = "N-1A",
            FilingDate = period.AddMonths(2),
            ReportEndingPeriod = period,
        };
        var fund = new NCenFundReport
        {
            RegistrantReportId = report.Id,
            FundId = accession + "-F1",
            SeriesId = seriesId,
            FundName = "Fund " + accession,
            RegistrantCik = registrantCik,
            AccessionNumber = accession,
            FilingDate = report.FilingDate,
            ReportEndingPeriod = period,
            FundTypes = NCenFundTypes.IndexFund,
            MonthlyAverageNetAssets = dailyAverage ? null : 100m,
            DailyAverageNetAssets = dailyAverage ? 100m : null,
            IsSuperseded = superseded,
        };
        fund.Advisers.Add(
            new NCenFundAdviser
            {
                FundReportId = fund.Id,
                Role = role,
                Name = "Adviser " + crd,
                Crd = crd,
            }
        );
        DbContext.AddRange(report, fund);
    }

    private async Task<Dictionary<Guid, InstitutionalHolderProfile>> Profiles()
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify
            .Set<InstitutionalHolderProfile>()
            .ToDictionaryAsync(profile => profile.InstitutionalHolderId);
    }

    private HolderProfileRefreshService Sut() =>
        new(Fixture.CreateDbContext(), NullLogger<HolderProfileRefreshService>());
}
