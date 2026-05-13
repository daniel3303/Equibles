using System.IO.Compression;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperTests {
    [Fact]
    public void FindEntry_LookupWithDifferentCase_FallsBackToCaseInsensitiveMatch() {
        // SEC publishes 13F-HR datasets where filename casing can shift
        // between periods (e.g. SUBMISSION.tsv in older dumps vs
        // submission.tsv in newer ones). The exact ZipArchive.GetEntry
        // call is case-sensitive, so FindEntry falls back to a case-
        // insensitive linear scan via FirstOrDefault. Pin the fallback
        // so a refactor that drops the `??` half (or replaces the scan
        // with a case-sensitive equality check) would silently make
        // every quarter where SEC swapped casing fail to import.
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            archive.CreateEntry("SUBMISSION.tsv");
        }
        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = HoldingsParsingHelper.FindEntry(readArchive, "submission.tsv");

        entry.Should().NotBeNull();
        entry.Name.Should().Be("SUBMISSION.tsv");
    }

    [Fact]
    public void TryParseDateOnly_SecFormat_ReturnsExpectedDate() {
        var success = HoldingsParsingHelper.TryParseDateOnly("15-MAR-2024", out var result);

        success.Should().BeTrue();
        result.Should().Be(new DateOnly(2024, 3, 15));
    }

    [Fact]
    public void ParseInvestmentDiscretion_DfndAbbreviation_ReturnsDefined() {
        // SEC 13F filings use abbreviated wire values for investment discretion:
        // "SOLE", "DFND" (defined investment discretion), and "OTR" (other). The C#
        // enum follows project standards by using full descriptive names — Sole,
        // Defined, Other — and `ParseInvestmentDiscretion` is the bridge that
        // translates each wire abbreviation to its domain value. The default arm
        // of the switch falls back to `InvestmentDiscretion.Sole`, so a regression
        // that drops the "DFND" case (or that renames the wire abbreviation in a
        // copy-paste edit) would silently reclassify every Defined-discretion
        // holding as Sole — corrupting the analytics that distinguish how managers
        // exercise authority over the holdings they report.
        //
        // The sibling [Fact] above pins the same wire-to-domain contract for the
        // "PRN" → Principal case in ParseShareType; this one extends it to the
        // structurally similar ParseInvestmentDiscretion switch.
        var result = HoldingsParsingHelper.ParseInvestmentDiscretion("DFND");

        result.Should().Be(InvestmentDiscretion.Defined);
    }

    [Fact]
    public void ParseInvestmentDiscretion_OtrAbbreviation_ReturnsOther() {
        // Sibling to `ParseInvestmentDiscretion_DfndAbbreviation_ReturnsDefined`.
        // Existing pin covers the DFND arm. This pin covers the OTR arm,
        // completing the three-mapped-arm-plus-default coverage matrix.
        //
        // Why OTR uniquely matters (and why pinning SOLE would be
        // tautological): the production switch has
        //   "SOLE" => Sole, "DFND" => Defined, "OTR" => Other, _ => Sole
        // The SOLE arm and the default arm BOTH return Sole. Pinning
        // SOLE specifically wouldn't catch a regression that drops only
        // the SOLE arm because the default would still produce the
        // correct Sole value. By contrast:
        //   - DFND arm (pinned by sibling): semantically distinct return
        //     value (Defined ≠ default Sole), so a dropped arm fails.
        //   - OTR arm (this pin): semantically distinct return value
        //     (Other ≠ default Sole), so a dropped arm fails.
        //
        // The risk this pin uniquely catches: a refactor that drops the
        // OTR arm under the false intuition that "Other is rare enough
        // to fall through to Sole" would compile, pass DFND, pass the
        // ParseShareType siblings, and silently reclassify every
        // "Other"-discretion holding as Sole. The 13F-HR `OTHER`
        // discretion category captures positions where the manager
        // explicitly disclaimed both sole AND shared/defined authority
        // (typically sub-advisor arrangements where another party
        // controls the actual trades). Misclassifying them as Sole
        // inflates the "manager fully controls" attribution that
        // ownership-influence analytics depend on.
        //
        // The complementary risk: a swap of the OTR arm with another
        // mapped value (e.g. `"OTR" => Defined` from a copy-paste edit
        // touching the wrong line) would still fail this pin because
        // the assertion targets the exact enum value Other.
        var result = HoldingsParsingHelper.ParseInvestmentDiscretion("OTR");

        result.Should().Be(InvestmentDiscretion.Other);
    }

    [Fact]
    public void ResolveManagerName_AccessionNotInOtherManagers_ReturnsNull() {
        // 13F filings list co-filing managers in a separate `OTHERMANAGER2.tsv` table
        // keyed by AccessionNumber → SequenceNumber → ManagerName. The vast majority
        // of filings are single-manager (no co-filers) and never produce an
        // OTHERMANAGER2 row, so ImportContext.OtherManagers has no key for those
        // accessions. ResolveManagerName guards every lookup with two layers of
        // TryGetValue so the missing-accession case returns null cleanly.
        //
        // The risk this test pins: a "simplification" refactor to direct indexer
        // access — `context.OtherManagers[accession][managerNumber.Value]` — would
        // throw KeyNotFoundException on every single-manager filing. That's the
        // common path in production: the importer batches submissions, and the
        // first co-filer-less filing would crash ImportDataSet, leaving the entire
        // quarterly dataset in a half-imported state with no recovery (the worker
        // marks ProcessedDataSet only on IsComplete = true).
        //
        // Choose a non-null managerNumber so the early `if (managerNumber == null)`
        // guard doesn't short-circuit — that path is trivial, the TryGetValue
        // chain is the dangerous one.
        var context = new ImportContext {
            OtherManagers = new Dictionary<string, Dictionary<int, string>>(),
        };

        var result = HoldingsParsingHelper.ResolveManagerName(context, "0001234-25-000001", managerNumber: 2);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseOptionType_CallWireValue_ReturnsCallEnum() {
        // Completes the ParseOptionType three-way coverage:
        //   - PUT → OptionType.Put         (sibling pin)
        //   - CALL → OptionType.Call       (this pin)
        //   - unrecognized → null          (existing pin)
        //
        // The PUT sibling and this CALL pin together pin the directional
        // distinction. The PUT pin alone could pass while CALL silently
        // returned null (if a regression dropped only the CALL arm),
        // because:
        //   - PUT pin proves the Put arm fires for "PUT".
        //   - Unrecognized pin proves null is returned for unknown values.
        //   - Without this CALL pin, dropping the CALL arm would route
        //     "CALL" to the default null branch — passing both existing
        //     pins while silently NULLING out every Call position in
        //     the 13F-HR dataset.
        //
        // The asymmetric risk that this pin uniquely catches: a refactor
        // that "consolidates" the two arms into a single regex-match
        // (e.g. `value.StartsWith("P") ? Put : null` — a "performance"
        // simplification that retains the PUT case) would compile, pass
        // the PUT and unrecognized pins, and silently demote every Call
        // position to null. Calls are the MAJORITY of 13F-HR option
        // positions (managers typically buy upside exposure via Calls
        // for index/sector beta), so this regression would empty out
        // the larger of the two derivative-exposure buckets.
        //
        // The complementary risk: a swap of the CALL arm with another
        // mapped value (e.g. `"CALL" => OptionType.Put` from a
        // copy-paste edit that touched the wrong arm) would invert
        // the signal — but that's the same swap risk the PUT sibling
        // explicitly documents. This pin completes the symmetric
        // coverage so the swap fails on BOTH ends.
        var result = HoldingsParsingHelper.ParseOptionType("CALL");

        result.Should().Be(OptionType.Call);
    }

    [Fact]
    public void ParseOptionType_PutWireValue_ReturnsPutEnum() {
        // Sibling to `ParseOptionType_UnrecognizedValue_ReturnsNullNotADefaultEnumValue`.
        // The existing pin covers the null-default branch. This pin covers the
        // "PUT" → OptionType.Put mapped arm — the high-business-value happy path.
        //
        // The risk this catches that the null-default sibling cannot: a
        // refactor that swapped the two mapped arms — `"PUT" => Call, "CALL"
        // => Put` — would compile, pass the unrecognized-default sibling
        // (it returns null either way regardless of the swap), and silently
        // INVERT every option position in the 13F-HR institutional-holdings
        // dataset. Downstream consequences cascade into derivative analytics:
        //
        //   • "Top hedgers" dashboards (filtered on Put positions) would
        //     surface managers who actually hold Calls — inverting the
        //     bearish/bullish signal.
        //   • Put/call ratio aggregates per manager would compute the
        //     inverse, breaking the contrarian-positioning indicator that
        //     several screener tools build on.
        //   • The institutional-holdings page's "options exposure" badge
        //     would mislabel every position card.
        //
        // The PUT arm is structurally adjacent to CALL in the switch source
        // (lines 49-50), making it the most likely casualty of a copy-paste
        // edit that touched the wrong line. The signal value of the inversion
        // is asymmetric — Put holdings are the rarer, more informative
        // signal (managers usually hedge upside via Calls, downside via
        // Puts) — so misclassifying Put → Call dilutes a high-signal
        // bucket into a higher-noise one.
        //
        // Pin uppercase "PUT" (the canonical SEC wire form per the 13F-HR
        // schema). ParseOptionType normalizes via ToUpperInvariant before
        // matching, but the documented contract is uppercase. Asserting
        // the exact enum value (Put, not the integer ordinal) guards
        // against enum-reorder refactors that would silently rebind
        // Put/Call to different underlying values.
        var result = HoldingsParsingHelper.ParseOptionType("PUT");

        result.Should().Be(OptionType.Put);
    }

    [Fact]
    public void ParseOptionType_UnrecognizedValue_ReturnsNullNotADefaultEnumValue() {
        // SEC 13F filings only carry an option-type wire value ("PUT" / "CALL") for actual
        // option positions; for the vast majority of holdings (common stock, ADRs, fund
        // shares, principal-denominated bonds) the field is empty or absent and `null`
        // is what flows downstream to InstitutionalHolding.OptionType.
        //
        // Critically, ParseOptionType's switch falls back to `return null` — UNLIKE its
        // structurally similar siblings ParseShareType (default → Shares) and
        // ParseInvestmentDiscretion (default → Sole), both of which intentionally swallow
        // unknown values into a "safest guess" enum. The asymmetry matters: the OptionType
        // column is nullable in the schema precisely because "this isn't an option" must
        // remain distinguishable from "this is an unrecognized option type". A copy-paste
        // refactor that "harmonizes" the default to `OptionType.Put` (or any other concrete
        // value) would silently reclassify every regular-stock holding as a Put position,
        // poisoning derivatives analytics across the entire dataset.
        //
        // Pin the null default on an unrecognized non-empty value (the wire emits "" for
        // non-options, but an unknown-but-non-empty input — e.g. a typo in a future SEC
        // schema change — is the sharper edge case).
        var result = HoldingsParsingHelper.ParseOptionType("WARRANT");

        result.Should().BeNull();
    }

    [Fact]
    public void ParseShareType_PrincipalAbbreviation_ReturnsPrincipal() {
        // SEC 13F filings use abbreviated wire values: "SH" for Shares, "PRN" for Principal.
        // The C# enum uses full descriptive names per project standards. ParseShareType is
        // the bridge that translates the wire abbreviation to the domain enum. The default
        // arm of the switch falls back to ShareType.Shares — so if the "PRN" → Principal
        // case were ever dropped (or the SEC-side abbreviation refactored to lowercase
        // without an `.ToUpperInvariant()` review), every Principal holding (typically
        // bond positions) would silently mis-classify as Shares. Pin the PRN mapping so
        // the wire-to-domain contract survives a switch-statement refactor.
        var result = HoldingsParsingHelper.ParseShareType("PRN");

        result.Should().Be(ShareType.Principal);
    }
}
