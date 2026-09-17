using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// PreserveStoredObservationKeys restores each incoming row's stored display ticker so a
/// presentation change cannot turn a replay into a second position. That retention can hand two
/// rows of one filing the same identity — a share class whose CUSIP keeps a ticker another CUSIP
/// now claims — and writing it would merge two securities into one row.
///
/// The refusal must NAME the conflict. The importer skips the filing and keeps going, so the
/// message is the only surviving record of which holder and security to repair; a bare "replay was
/// refused" cost a forensic pass over the whole holdings table to identify two rows.
/// </summary>
public class HoldingsImportServiceRetainedIdentityCollisionTests
{
    private static readonly Guid Issuer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Holder = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static InstitutionalHolding Row(string cusip, string listedTicker) =>
        new()
        {
            EquityIssuerId = Issuer,
            InstitutionalHolderId = Holder,
            ReportDate = new DateOnly(2023, 9, 30),
            Cusip = cusip,
            ListedTicker = listedTicker,
            ShareType = ShareType.Shares,
            OptionType = null,
            FilingType = FilingType.Form13F,
        };

    [Fact]
    public void FindRetainedIdentityCollision_TwoRowsRetainedOntoOneTicker_NamesBothPositions()
    {
        // Brown-Forman's presentation flipped: the class-A CUSIP keeps the stored "primary"
        // ticker while the class-B CUSIP now resolves to primary too, so both land on one key.
        var collision = HoldingsImportService.FindRetainedIdentityCollision([
            Row("115637100", null),
            Row("115637209", null),
        ]);

        Assert.NotNull(collision);
        Assert.Contains("115637100", collision.Message);
        Assert.Contains("115637209", collision.Message);
        Assert.Contains(Issuer.ToString(), collision.Message);
        Assert.Contains(Holder.ToString(), collision.Message);
        Assert.Contains("2023-09-30", collision.Message);
    }

    [Fact]
    public void FindRetainedIdentityCollision_SiblingClassesKeepDistinctTickers_ReturnsNull()
    {
        // The same two CUSIPs once their stored identity is correct: one primary, one BF-A.
        // Two share classes of one issuer are two positions, not a conflict.
        Assert.Null(
            HoldingsImportService.FindRetainedIdentityCollision([
                Row("115637100", "BF-A"),
                Row("115637209", null),
            ])
        );
    }

    [Fact]
    public void FindRetainedIdentityCollision_OptionLegBesideItsShareRow_ReturnsNull()
    {
        // OptionType is part of the position grain, so a call leg and the share row under one
        // ticker are distinct positions the guard must not refuse.
        var optionLeg = Row("115637209", null);
        optionLeg.OptionType = OptionType.Call;

        Assert.Null(
            HoldingsImportService.FindRetainedIdentityCollision([Row("115637209", null), optionLeg])
        );
    }
}
