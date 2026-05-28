using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceBuildHoldingKeyCultureInvarianceTests
{
    // Adversarial Lane A. BuildHoldingKey interpolates the reportDate
    // segment via `$"{...|{reportDate}|...}"`. DateOnly's default
    // ToString() formats with the thread CurrentCulture's short date
    // pattern: en-US/Invariant render 2024-12-31 as "12/31/2024",
    // de-DE renders "31.12.2024", fr-FR renders "31/12/2024". The
    // string returned by BuildHoldingKey is therefore host-locale
    // dependent.
    //
    // Sibling pins (ShareType / OptionType differentiation) cover
    // segments that don't go through culture-formatted ToString.
    // None of them switch CurrentCulture, so the entire suite passes
    // on the runner's default culture and the latent culture coupling
    // stays invisible.
    //
    // The contract this pin checks (derived from the repo's other
    // culture-invariance pins — cf. ShortDataTools.FormatSignedChange
    // sibling and the BaseScraperWorker.FormatInterval fix in #2426 —
    // and from the function's role as a dedup key): the key emitted
    // for a given (stockId, holderId, reportDate, shareType,
    // optionType, filingType) tuple MUST be byte-identical regardless
    // of CurrentCulture. A culture-dependent key forks the
    // entriesByKey dictionary lookup against the dbHolding side
    // (HoldingsImportService.cs:837) the moment any deploy ships
    // with a different host locale than the one used to populate
    // the dict, silently dropping every existing-row match into the
    // "new row" arm — duplicating institutional holdings rows in the
    // database with no surfaced error.
    //
    // Test strategy mirrors the FormatSignedChange culture-invariance
    // sibling: capture CurrentCulture, switch to de-DE (the canonical
    // non-Invariant locale used across the repo's other culture pins,
    // and the one whose `.` thousand/short-date separator is most
    // visually distinct from Invariant's `/`), reflection-invoke the
    // private static, restore in finally. Compare against the
    // Invariant-culture key for the same inputs. Failure manifests as
    // the date-segment separator/order swap (".12.31.2024." vs
    // ".12/31/2024.").
    [Fact(
        Skip = "GH-2594 — BuildHoldingKey's reportDate segment renders host-locale short-date pattern (de-DE 31.12.2024 vs Invariant 12/31/2024); dedup-key forks by culture"
    )]
    public void BuildHoldingKey_UnderNonInvariantCulture_RendersReportDateSegmentCultureInvariantly()
    {
        var method = typeof(HoldingsImportService).GetMethod(
            "BuildHoldingKey",
            BindingFlags.NonPublic | BindingFlags.Static,
            [
                typeof(Guid),
                typeof(Guid),
                typeof(DateOnly),
                typeof(ShareType),
                typeof(OptionType?),
                typeof(FilingType),
            ]
        );

        var stockId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 12, 31);
        object[] args =
        [
            stockId,
            holderId,
            reportDate,
            ShareType.Shares,
            (OptionType?)null,
            FilingType.Form13F,
        ];

        var original = CultureInfo.CurrentCulture;
        string invariantKey;
        string deDeKey;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantKey = (string)method.Invoke(null, args);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeKey = (string)method.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        deDeKey
            .Should()
            .Be(
                invariantKey,
                "the holding dedup key must be byte-identical across host locales; DateOnly's default ToString() uses CurrentCulture's short date pattern (de-DE → 31.12.2024, Invariant → 12/31/2024), so a deploy under a different culture than the dict was populated under silently misses every dedup match and duplicates rows"
            );
    }
}
