using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// A stored filed type is written only when a new filing is extracted, so an issuer that stopped
/// filing keeps whatever the classifier said years ago. Pins that the re-stamp brings such rows to
/// the current classification (an exchange-traded note stored as Other becomes debt) and leaves
/// rows that already agree untouched.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class RegistrationTypeRestampTests : ParadeDbMcpTestBase
{
    public RegistrationTypeRestampTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Apply_StaleStoredClassification_RestampsOnlyTheRowsThatDisagree()
    {
        var note = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "1053092",
            Ticker: "GLDI",
            SharesOutStanding: 4_399_680_200,
            ListedSecurityType: ListedSecurityType.Other,
            ListedSecurityTitle: "Credit Suisse X-Links® Gold Shares Covered Call ETNs due February 2, 2033"
        );
        var common = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "1730168",
            Ticker: "AVGO",
            ListedSecurityType: ListedSecurityType.CommonShares,
            ListedSecurityTitle: "Common Stock, par value $0.001 per share"
        );
        var untitled = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "2",
            Ticker: "NOTE",
            ListedSecurityType: ListedSecurityType.Other
        );
        DbContext.AddRange(note, common, untitled);
        await DbContext.SaveChangesAsync();

        (await RegistrationTypeRestamp.Apply(DbContext)).Should().Be(1);
        (await RegistrationTypeRestamp.Apply(DbContext)).Should().Be(0);

        await using var verify = Fixture.CreateDbContext();
        var types = await verify
            .Set<EquitySecurity>()
            .AsNoTracking()
            .ToDictionaryAsync(
                security => security.EquityIssuerId,
                security => security.RegistrationType
            );
        types[note.Id].Should().Be(ListedSecurityType.DebtSecurities);
        types[common.Id].Should().Be(ListedSecurityType.CommonShares);
        types[untitled.Id].Should().Be(ListedSecurityType.Other);
    }
}
