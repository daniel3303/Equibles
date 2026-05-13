using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.RegularExpressions;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Tests for <see cref="HouseDisclosureClient"/>. The public entry points pull PDFs from the
/// House Clerk site, so we exercise the pure-logic private regex helpers via reflection.
/// </summary>
public class HouseDisclosureClientTests {
    private static readonly MethodInfo ExtractTransactionTypeMethod = typeof(HouseDisclosureClient)
        .GetMethod("ExtractTransactionType", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo RemoveTrailingTransactionTypeMethod = typeof(HouseDisclosureClient)
        .GetMethod("RemoveTrailingTransactionType", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo OwnerCodeRegexMethod = typeof(HouseDisclosureClient)
        .GetMethod("OwnerCodeRegex", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo DatePatternRegexMethod = typeof(HouseDisclosureClient)
        .GetMethod("DatePatternRegex", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void DatePatternRegex_MatchesMmDdYyyyDateInPdfLineText() {
        // DatePatternRegex pattern: `\b(\d{2}/\d{2}/\d{4})\b`. Used by
        // ParseTransactionLines to locate the transaction date within a PDF
        // text line that mixes owner code, asset description, transaction
        // type marker, and date — e.g. "SP APPLE INC - COMMON STOCK P 01/14/2025".
        // Without a match the line is skipped (`if (!dateMatch.Success) continue;`);
        // with a match, the captured date string flows into ParseDate to become
        // the persisted TransactionDate.
        //
        // The risk: a refactor that drops the `\b` word boundaries (e.g. "tidy
        // up" the pattern to `(\d{2}/\d{2}/\d{4})`) would silently match
        // date-looking substrings inside longer numeric runs — `1234/56/789012`
        // would produce a phantom "34/56/7890" capture. ParseDate would reject
        // most such captures, but borderline cases could produce wrong dates
        // that downstream date-window analytics ("trades within N days of a
        // hearing") would silently mis-bucket.
        //
        // Sibling to OwnerCodeRegex_MatchesSpouseCodeSP. Same reflection pattern
        // (private static partial Regex via GeneratedRegex). Assert both
        // Success AND the captured date string so a regression that broke the
        // capture group (e.g. `(?:...)` non-capturing) also fails.
        var regex = (Regex)DatePatternRegexMethod.Invoke(null, null);

        var match = regex.Match("SP APPLE INC - COMMON STOCK P 01/14/2025 $1,001 - $15,000");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("01/14/2025");
    }

    [Fact]
    public void OwnerCodeRegex_MatchesSpouseCodeSP_AndCapturesOwnerCode() {
        // OwnerCodeRegex pattern: `^(SP|JT|DC|Self)\b` (case-insensitive).
        // The four alternatives are the four House PTR owner codes:
        //   • SP   — Spouse
        //   • JT   — Joint (member + spouse)
        //   • DC   — Dependent Child
        //   • Self — the member
        // Each is independently load-bearing: ParseTransactionLines walks every PDF
        // line through this regex, skips lines that don't match, and uses the
        // capture group as the OwnerType field. A regression that drops any single
        // alternative (e.g. "let me alphabetize the alternation" reorder that
        // typos one out, or a "consolidate to SP|JT" simplification) would silently
        // drop every PTR row attributed to that owner code from the import.
        //
        // The existing pins in this file cover ExtractTransactionType and
        // RemoveTrailingTransactionType but NOT OwnerCodeRegex. Spouse (SP) is
        // the highest-volume non-Self owner code in the House PTR corpus
        // (members frequently trade through joint or spouse-attributed accounts
        // for ethics-reporting reasons). Pin SP specifically so a regression that
        // drops the SP arm of the alternation surfaces here rather than as
        // silently-missing spouse-attributed trades in the dashboard.
        //
        // Two assertions: the regex matched (Success) AND the captured group
        // value is exactly "SP" (so a regression that broke the capture group
        // structure — e.g. changing `(SP|...)` to `(?:SP|...)` non-capturing —
        // would also fail). The downstream `ownerMatch.Groups[1].Value` flows
        // straight into the persisted OwnerType column.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("SP APPLE INC - COMMON STOCK P 01/14/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("SP");
    }

    [Fact]
    public void OwnerCodeRegex_MatchesJointCodeJT_AndCapturesOwnerCode() {
        // Second sibling in the OwnerCodeRegex alternation family. The existing
        // SP pin asserts the Spouse arm; this pin asserts the Joint arm — the
        // second-most-common House PTR owner code after Self.
        //
        // Why JT specifically (and why it's unreachable from the SP sibling):
        //   OwnerCodeRegex is `^(SP|JT|DC|Self)\b` with case-insensitive matching.
        //   Each alternative is INDEPENDENTLY load-bearing — the regex engine
        //   short-circuits on the FIRST successful alternative, so the SP arm
        //   only ever fires for SP inputs. A regression that drops the JT arm
        //   (e.g. "let me reorder the alternation by frequency and drop the
        //   middle one by mistake" — a real refactor pattern) would compile
        //   cleanly, pass the SP pin (the input is "SP ...", SP arm fires),
        //   pass the ExtractTransactionType and RemoveTrailingTransactionType
        //   pins (they don't use OwnerCodeRegex), and silently drop every
        //   joint-attributed trade from the House PTR import.
        //
        // The production analog is concrete: ParseTransactionLines walks every
        // PDF line, the OwnerCodeRegex match is the gate for "is this line a
        // transaction row?" If JT lines fail the gate, the entire row is
        // dropped — no error, no log warning, just missing data. Joint accounts
        // are the standard structure for House members who trade through
        // shared marital brokerage accounts (the default ethics structure for
        // dual-income households), so dropping JT silently erases the dominant
        // owner-code population for married members.
        //
        // The semantic distinction between SP and JT matters downstream:
        //   • SP (Spouse) — trade is in the spouse's account only. Member has
        //     no direct economic interest; reportable but typically excluded
        //     from "member's personal trading" influence-analytics.
        //   • JT (Joint) — trade is in a shared account; member shares the
        //     economic position 50/50 (or similar). Counts toward the member's
        //     personal trading attribution.
        // Misclassifying JT as SP (or dropping JT entirely) flips the
        // influence-analytics signal for every joint-attributed position.
        //
        // Pin uppercase "JT" (canonical case the PDFs emit) and assert both
        // Success AND the capture group value is exactly "JT". The dual
        // assertion proves (a) the alternation arm matched and (b) the
        // capture-group structure is intact — a refactor that changed
        // `(SP|JT|DC|Self)` to `(?:SP|JT|DC|Self)` non-capturing would still
        // match but would fail `Groups[1].Value.Should().Be("JT")`.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("JT TESLA INC - COMMON STOCK P 03/22/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("JT");
    }

    [Fact]
    public void OwnerCodeRegex_MatchesDependentChildCodeDC_AndCapturesOwnerCode() {
        // Third sibling in the OwnerCodeRegex alternation family. The existing
        // pins assert SP (Spouse) and JT (Joint). This pin asserts DC (Dependent
        // Child) — the third alternative in `^(SP|JT|DC|Self)\b`.
        //
        // Why DC specifically (and why it's unreachable from SP/JT siblings):
        //   The regex short-circuits on the first matching alternative, so the
        //   SP arm only fires for SP inputs, the JT arm only for JT, the DC
        //   arm only for DC, etc. A regression that drops the DC arm (e.g. a
        //   "consolidate the family-attributed codes into JT" refactor under
        //   the false intuition that "JT and DC both flag dependent attribution,
        //   merge them") would compile, pass the SP and JT pins (their own
        //   arms still fire), pass every ExtractTransactionType /
        //   RemoveTrailingTransactionType pin (none use OwnerCodeRegex), and
        //   silently drop every Dependent Child–attributed trade from the
        //   House PTR import.
        //
        // The semantic distinction matters downstream:
        //   • DC (Dependent Child) — trade is in a child's account that the
        //     member holds custodial responsibility for. House ethics rules
        //     require disclosure but treat the holding as the child's, not
        //     the member's, for personal-trading influence analysis.
        //   • JT (Joint) — shared account, member shares the economic position.
        //   • SP (Spouse) — spouse-only account.
        //   The three are NOT interchangeable; conflating DC with JT would
        //   incorrectly attribute child-account trades to the member's
        //   personal portfolio in influence-analytics, inflating the
        //   member's apparent trading activity.
        //
        // The production analog: ParseTransactionLines walks every PDF line
        // through OwnerCodeRegex and uses the capture group as the OwnerType
        // column. A failed alternation match drops the entire row — no log,
        // no error, just missing data. Dependent-child accounts are less
        // common than JT but still represent a meaningful population
        // (members of Congress with minor children routinely report
        // through custodial accounts under federal disclosure rules).
        //
        // Pin uppercase "DC" (canonical case the PDFs emit) and assert both
        // Success AND the capture group value is exactly "DC". The dual
        // assertion proves (a) the alternation arm matched and (b) the
        // capture-group structure survives — a refactor to non-capturing
        // `(?:SP|JT|DC|Self)` would still match but fail
        // `Groups[1].Value.Should().Be("DC")`.
        //
        // With this pin, three of four OwnerCodeRegex alternation arms are
        // individually pinned (SP, JT, DC). The fourth, "Self" (the member
        // themselves), is the natural-extension target for a future iteration
        // and would close the family.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("DC MICROSOFT CORP - COMMON STOCK P 06/10/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("DC");
    }

    [Fact]
    public void OwnerCodeRegex_MatchesMemberCodeSelf_AndCapturesOwnerCode() {
        // Fourth and final sibling in the OwnerCodeRegex alternation family. With
        // this pin, all four alternatives in `^(SP|JT|DC|Self)\b` are individually
        // pinned: SP (Spouse), JT (Joint), DC (Dependent Child), Self (the member).
        //
        // Why "Self" uniquely matters and is unreachable from the other three pins:
        //   The regex short-circuits on the first matching alternative, so the
        //   "Self" arm only ever fires for Self inputs. The pattern is also case-
        //   insensitive (RegexOptions.IgnoreCase) so "self" / "SELF" / "Self"
        //   should all match. A regression that drops the "Self" arm (e.g. a
        //   "remove redundant Self code since it's the default attribution"
        //   refactor — Self IS the implicit default in regulatory framing but
        //   it's still emitted as an explicit literal in the PDF) would compile,
        //   pass the SP/JT/DC pins, pass every ExtractTransactionType /
        //   RemoveTrailingTransactionType pin, and silently drop every member-
        //   attributed trade from the import.
        //
        // The production volume implications make this the LARGEST silent-loss
        // regression of the four arms:
        //   • Self (the member) — DOMINANT House PTR owner code. Most members
        //     trade primarily through their own personal accounts; Self lines
        //     outnumber SP+JT+DC combined for the typical member.
        //   • SP/JT/DC — household-attributed accounts, secondary populations.
        //   Dropping Self would silently erase the bulk of the House PTR
        //   dataset — every member's personal trading would vanish from the
        //   ingest, leaving only household-attributed trades. The remaining
        //   dataset would look surprisingly small but plausible; operator
        //   inspection of "active members" would still show data, just at
        //   ~20% of the true volume. Exactly the silent partial-failure
        //   mode that CI must catch.
        //
        // Self is structurally distinct from the three two-letter codes in
        // one other way: it's the only multi-character alternative (4 chars
        // vs 2 chars for SP/JT/DC). A refactor that "normalize all owner
        // codes to two-character abbreviations" — perhaps replacing
        // `(SP|JT|DC|Self)` with `(SP|JT|DC|SE)` to make them uniform —
        // would silently miss every Self line in the PDFs (the PDFs emit
        // "Self", not "SE"). The two-pin pair (any two-letter sibling +
        // this one) catches the unification refactor; this pin alone
        // catches the dropped-Self refactor.
        //
        // Pin "Self" with mixed-case capitalization (the canonical case the
        // House PDFs emit — first letter uppercase, rest lowercase) and assert
        // both Success AND the capture group value is exactly "Self". The dual
        // assertion proves (a) the alternation arm matched, (b) the case
        // preserved in the capture group, and (c) the capture-group structure
        // is intact.
        var regex = (Regex)OwnerCodeRegexMethod.Invoke(null, null);

        var match = regex.Match("Self NVIDIA CORP - COMMON STOCK P 09/05/2025");

        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("Self");
    }

    [Fact]
    public void ExtractTransactionType_PurchaseTrailingP_ReturnsPurchase() {
        // House PTRs encode purchases as a trailing bare "P" (no qualifier). The
        // PurchaseTypeRegex (`\bP\s*$`) is the only matcher for that path — if a
        // regression typo'd it to `^P\b` (anchored to start) or dropped the
        // word-boundary, every Purchase transaction would silently classify as null
        // and be dropped by the ProcessRow loop in CongressionalTradeSyncService.
        // The companion Sale [Fact] covers the more complex SaleTypeRegex; this one
        // pins the simpler Purchase branch — equally load-bearing, currently
        // unpinned, and trivial to regress because the regex is one line away.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL P"]);

        result.Should().Be(CongressTransactionType.Purchase);
    }

    [Fact]
    public void ExtractTransactionType_LowercaseSaleS_StillReturnsSaleViaIgnoreCase() {
        // SaleTypeRegex is declared with `RegexOptions.IgnoreCase` — PurchaseTypeRegex
        // is NOT. The asymmetry is deliberate (House PTRs always emit uppercase "P"
        // and "S" so case-insensitivity has no production value for either, but the
        // SaleTypeRegex's IgnoreCase was added defensively when the (partial)/(full)
        // parenthetical groups were introduced, and PurchaseTypeRegex stayed strict).
        //
        // The risk this pin catches: a refactor that "harmonizes" the two regexes
        // by either DROPPING IgnoreCase from SaleTypeRegex (under the assumption
        // that PurchaseTypeRegex's case-sensitivity is the convention) or ADDING
        // IgnoreCase to PurchaseTypeRegex (consistency change). The dropping case
        // is the dangerous one: lowercase "s" rows in any future aggregator-emitted
        // PTR (e.g., a third-party scraper that normalizes casing before re-publishing)
        // would silently classify as null and be dropped from the import.
        //
        // No other test in this file exercises the IgnoreCase modifier directly —
        // every existing sibling uses uppercase letters. Pin lowercase "s" so the
        // modifier can't be removed silently. The companion bare-S pin already
        // proves uppercase "S" matches; this pin proves the case-insensitive
        // alternative also matches.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL s"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ExtractTransactionType_SaleWithPartialQualifier_ReturnsSale() {
        // House PTRs encode partial sales as "S (partial)" rather than bare "S".
        // The SaleTypeRegex must accept both forms — if a regression tightens it to
        // require the parenthetical (or, conversely, drops the optional group),
        // half the House sale transactions get classified as null and are silently
        // dropped by the importer. Pin the qualified-form match so the regex can't
        // narrow without a test failure.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL S (partial)"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ExtractTransactionType_SaleWithFullQualifier_ReturnsSale() {
        // Sibling to the existing "S (partial)" pin. The SaleTypeRegex alternation
        // `(?:partial|full)` covers BOTH qualifier forms — a refactor that simplifies
        // the group to just `partial` (e.g. someone "cleaning up" what looks like an
        // unused alternation arm, or copying the pattern incorrectly into a sibling
        // regex) would still pass the existing partial-qualifier test but silently
        // drop every "S (full)" sale transaction. House PTRs use BOTH forms in
        // production: "(full)" appears whenever a member liquidates an entire
        // position, "(partial)" for fractional sells. The two qualifier arms are
        // independent — partial-test coverage does NOT prove full-test coverage.
        // Without this pin, a regex that only accepted one arm would compile cleanly,
        // pass the existing tests, and silently halve House sale visibility while
        // the partial-test would still report a green build.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["TSLA S (full)"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void ExtractTransactionType_BareSaleNoQualifier_ReturnsSale() {
        // Completes the Sale/Purchase pin family. Existing pins cover:
        //   • bare "P"        → Purchase  (ExtractTransactionType_PurchaseTrailingP_…)
        //   • "S (partial)"   → Sale      (existing partial pin)
        //   • "S (full)"      → Sale      (existing full pin)
        // The bare "S" form — `S` at line end with NO parenthetical qualifier — is
        // the most common House PTR sale encoding (used for ordinary whole-position
        // sells where no partial/full qualifier applies), and it's the ONLY form
        // currently unpinned in the family.
        //
        // The SaleTypeRegex pattern is `\bS\s*(\((?:partial|full)\))?\s*$` — the
        // `?` modifier on the parenthetical group is what makes bare S match. A
        // regression that drops the `?` (someone "tightening" the pattern under
        // the false assumption that every Sale must carry a qualifier) would still
        // pass BOTH the partial AND the full sibling pins — those rows have the
        // parenthetical present — while silently classifying every bare-S row as
        // null, dropping them from the import via the `if (txType == null) continue;`
        // guard in ParseTransactionLines. The failure mode is invisible: the import
        // succeeds, just with a fraction of the expected row count, and the
        // dashboard quietly shows fewer insider sales than reality.
        //
        // Pin the bare-S case specifically so the optional-qualifier `?` modifier
        // can't be removed without a test failure.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(null, ["AAPL S"]);

        result.Should().Be(CongressTransactionType.Sale);
    }

    [Fact]
    public void RemoveTrailingTransactionType_SaleWithPartialQualifier_StripsSuffixAndQualifier() {
        // After ExtractTransactionType recognises the trailing "S (partial)" marker,
        // RemoveTrailingTransactionType has to strip it cleanly so the asset name
        // ("APPLE INC - COMMON STOCK") doesn't carry a dangling " S (partial)" tail
        // into the persisted AssetName column. The two regexes (Sale/Purchase) must
        // be applied in sequence and the result trimmed — without this, downstream
        // consumers (UI, MCP tools) display garbled asset names. Pin the
        // parenthetical-qualifier strip so the regex can't be loosened to a bare
        // "S" matcher that would leave the "(partial)" tail behind.
        var result = (string)RemoveTrailingTransactionTypeMethod.Invoke(null, ["APPLE INC - COMMON STOCK S (partial)"]);

        result.Should().Be("APPLE INC - COMMON STOCK");
    }

    [Fact]
    public void RemoveTrailingTransactionType_PurchaseTrailingP_StripsSuffix() {
        // Sibling to the existing Sale pin. RemoveTrailingTransactionType applies
        // BOTH SaleTypeRegex.Replace AND PurchaseTypeRegex.Replace in sequence —
        // the existing Sale pin only exercises the Sale arm. A regression that
        // drops the PurchaseTypeRegex().Replace() call (plausible during a
        // refactor that consolidates the two replaces into a single combined
        // regex) would slip past the Sale pin entirely: Sale-tagged rows still
        // get stripped, but every Purchase row keeps a dangling " P" tail in
        // the persisted AssetName. Downstream the asset name column reads
        // "APPLE INC - COMMON STOCK P" — visible in the Congress trades UI
        // and MCP tool outputs. This pin exercises the Purchase strip in
        // isolation (no S in the input) so the Purchase replace can't be
        // silently dropped or merged with the Sale replace in a way that
        // changes its semantics. The PurchaseTypeRegex `\bP\s*$` is
        // intentionally case-sensitive (unlike SaleTypeRegex's IgnoreCase),
        // pin asserts the canonical capital-P form rather than mixed case.
        var result = (string)RemoveTrailingTransactionTypeMethod.Invoke(null, ["MICROSOFT CORP - COMMON STOCK P"]);

        result.Should().Be("MICROSOFT CORP - COMMON STOCK");
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipReturns404ForYear_ReturnsEmptyListWithoutThrowing() {
        // First HTTP-coupled pin in this file. Every existing test targets a private
        // regex helper via reflection; the 51 missed lines in HouseDisclosureClient
        // sit in the public GetRecentTransactions flow and its private HTTP helpers
        // (DownloadAndParseFilingIndex, DownloadAndParsePtrPdf, SendWithRetryAsync).
        // This pin exercises the load-bearing 404 short-circuit in
        // DownloadAndParseFilingIndex:
        //   if (response.StatusCode == HttpStatusCode.NotFound) {
        //       _logger.LogDebug("House FD ZIP not found for year {Year}", year);
        //       return [];
        //   }
        // House FD ZIPs don't exist for years before the disclosure program started
        // and may not be published yet for the current year before the first member
        // files. A 404 is the COMMON case for valid-but-empty years — not an error.
        // Without the short-circuit, `response.EnsureSuccessStatusCode()` on the next
        // line throws HttpRequestException, which propagates into
        // GetRecentTransactions' outer catch (Exception) → logs a Warning per year.
        // Operators' dashboards then show "Failed to download House filing index"
        // warnings for every absent year on every run, drowning genuine HTTP errors
        // in noise.
        //
        // A refactor that "simplifies" DownloadAndParseFilingIndex by removing the
        // 404 branch (under the false assumption that EnsureSuccessStatusCode +
        // the outer try/catch handles all status codes uniformly) would compile,
        // pass every existing regex pin (they don't touch HTTP), and silently
        // promote every empty-year 404 from a quiet DEBUG to a WARNING. The data
        // outcome is the same (empty list), so a test asserting only on the
        // returned list would miss the regression — but the operational outcome
        // diverges: log volume balloons, real failures get buried, on-call
        // pages on noise.
        //
        // Pin a single-year request against an HttpClient backed by a stub handler
        // that returns 404 for every URL. The assertion is on the returned list
        // (empty) AND that no exception is thrown — together they prove the
        // method handles the 404 path internally rather than relying on the
        // outer catch as a fallback.
        var handler = new ConstantStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(httpClient, Substitute.For<ILogger<HouseDisclosureClient>>());

        var result = await sut.GetRecentTransactions(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            CancellationToken.None);

        result.Should().BeEmpty();
        handler.Requests.Should().ContainSingle(
            "only the year's FD ZIP should be requested — a 404 must not cascade into per-filing PDF downloads");
    }

    private sealed class ConstantStatusHandler : HttpMessageHandler {
        private readonly HttpStatusCode _statusCode;
        public List<string> Requests { get; } = new();
        public ConstantStatusHandler(HttpStatusCode statusCode) => _statusCode = statusCode;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipMissingExpectedXmlEntry_ReturnsEmptyListAndDoesNotRequestAnyPtrPdf() {
        // Second HTTP-coupled pin in this file. Sibling to
        // GetRecentTransactions_FdZipReturns404ForYear_ReturnsEmptyListWithoutThrowing.
        // That test exercises the 404 short-circuit BEFORE the ZIP is opened.
        // This one covers the structurally distinct branch INSIDE
        // DownloadAndParseFilingIndex after a successful 200 response:
        //   var xmlEntry = archive.GetEntry($"{year}FD.xml");
        //   if (xmlEntry == null) {
        //       _logger.LogWarning("No XML index found in House FD ZIP for year {Year}", year);
        //       return [];
        //   }
        // The two early-return branches (404 status, missing XML entry) are
        // independent guards — the 404 sibling never reaches the ZipArchive
        // open call, so it doesn't exercise this branch.
        //
        // Production scenario: the House Clerk occasionally publishes a
        // partial-build FD ZIP during the rollover window where the
        // {year}FD.xml entry has been renamed (a past rebrand shipped
        // "{year}_FD.xml" with an underscore for half a day before being
        // rolled back), or the archive is uploaded with a leading
        // directory prefix that breaks GetEntry's exact-name match. The
        // guard ensures the importer silently skips that year rather than
        // dereferencing a null xmlEntry on the next line:
        //   await using var xmlStream = xmlEntry.Open();
        // which would NRE, propagate up through GetRecentTransactions'
        // outer catch (Exception), and log a Warning per affected year.
        //
        // A refactor that drops the null guard (e.g. a "use the null-
        // forgiving operator since we just downloaded the file" simpli-
        // fication) would compile cleanly, pass the 404 sibling (different
        // code path entirely), and silently crash every year with a
        // misnamed XML entry. The data outcome is the same (no filings
        // imported for that year), but the operational signal — DEBUG vs.
        // crash-warning per malformed-but-published ZIP — diverges
        // exactly the way that masks real CDN incidents.
        //
        // Pin: build a one-entry ZIP in memory with an UNEXPECTED entry
        // name ("wrongname.xml") and serve it as the response body for
        // the year's FD ZIP request. Three assertions together prove the
        // missing-entry guard fires:
        //   1. No exception escapes — proves GetEntry's null result was
        //      handled before the .Open() dereference.
        //   2. The returned filings list is empty — proves the method
        //      returned [] from the guard rather than processing some
        //      fallback entry.
        //   3. Only the ZIP URL was requested — proves the missing-XML
        //      branch did NOT cascade into PTR PDF downloads (which
        //      would happen if the method somehow surfaced phantom
        //      filings from another path).
        var zipBytes = BuildZipWithSingleEntry("wrongname.xml", "<irrelevant />");
        var handler = new BytesContentHandler(zipBytes, "application/zip");
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(httpClient, Substitute.For<ILogger<HouseDisclosureClient>>());

        var result = await sut.GetRecentTransactions(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31),
            CancellationToken.None);

        result.Should().BeEmpty();
        handler.Requests.Should().ContainSingle(
            "the missing-XML-entry branch must not cascade into per-filing PTR PDF downloads");
        handler.Requests[0].Should().Contain("2025FD.zip",
            "only the year's FD ZIP URL should be requested");
    }

    private static byte[] BuildZipWithSingleEntry(string entryName, string entryContent) {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true)) {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(entryContent);
        }
        return memory.ToArray();
    }

    private sealed class BytesContentHandler : HttpMessageHandler {
        private readonly byte[] _content;
        private readonly string _contentType;
        public List<string> Requests { get; } = new();
        public BytesContentHandler(byte[] content, string contentType) {
            _content = content;
            _contentType = contentType;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new ByteArrayContent(_content)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(_contentType);
            return Task.FromResult(response);
        }
    }

    [Fact]
    public void ParseTransactionLines_RealisticHousePtrPurchaseLine_ProducesFullyPopulatedTransaction() {
        // ParseTransactionLines is the central private orchestrator that ties together
        // every regex helper in this class (OwnerCodeRegex, DatePatternRegex,
        // PurchaseTypeRegex/SaleTypeRegex via ExtractTransactionType /
        // RemoveTrailingTransactionType) PLUS three pure helpers from
        // DisclosureParsingHelper (ExtractTickerFromAssetName, ParseDate,
        // ParseAmountRange, Truncate). The existing pins in this file each prove a
        // single regex in isolation, but none of them prove the end-to-end pipeline
        // — i.e. that the orderings, substring slices, and field assignments inside
        // ParseTransactionLines wire the helpers together correctly into a populated
        // DisclosureTransaction.
        //
        // The risk this catches: a refactor that swaps two of the helper calls,
        // re-orders the `remainder[..dateMatch.Index]` slice, or mis-assigns one of
        // the ten DisclosureTransaction property setters (e.g. Ticker ↔ OwnerType
        // swap because both are short strings, or AmountFrom ↔ AmountTo flip because
        // the regex matches return them in a single ordered list) would pass every
        // existing regex-level pin (each helper still works in isolation) AND would
        // pass `dotnet build` (the types align) while silently corrupting every
        // House PTR row in the imported dataset. The corruption would surface in
        // dashboards as "every member trades AAPL" (ticker becomes owner code) or
        // "every trade is $0–$1,001" (amount range inverted) — visible degradations
        // but not a build break. This test pins the full pipeline against one
        // canonical realistic line so any wiring regression fails here.
        //
        // The canonical House PTR PDF line format (as emitted by the SEC EDGAR
        // PtrPdfUrlTemplate at disclosures-clerk.house.gov):
        //   "{owner-code} {asset-name-with-(TICKER)} {tx-type} {MM/DD/YYYY} {$amt-from} - {$amt-to}"
        // Every field in the line maps directly to a DisclosureTransaction column:
        //   • SP                       → OwnerType
        //   • APPLE INC (AAPL)         → AssetName + Ticker (via parenthetical extraction)
        //   • P                        → TransactionType (Purchase)
        //   • 01/14/2025               → TransactionDate
        //   • $1,001 - $15,000         → AmountFrom + AmountTo
        // Plus two filing-level fields fed via the HouseFiling record:
        //   • filing.MemberName        → MemberName
        //   • filing.FilingDate        → FilingDate
        // Position is hardcoded to Representative (House is the House of Representatives).
        //
        // Reflection scaffolding mirrors the existing private-static patterns in
        // this file but ALSO needs to (a) construct a HouseDisclosureClient
        // instance because ParseTransactionLines is an INSTANCE method and (b)
        // construct a HouseFiling instance because HouseFiling is a private
        // nested record. The constructor takes HttpClient + ILogger — neither is
        // touched by ParseTransactionLines (no HTTP, no logging in this path), so
        // a fresh HttpClient and NullLogger suffice. The HouseFiling record is
        // positional: (MemberName, DocId, FilingDate, StateDst).
        var parseTransactionLines = typeof(HouseDisclosureClient).GetMethod(
            "ParseTransactionLines", BindingFlags.NonPublic | BindingFlags.Instance);
        var houseFilingType = typeof(HouseDisclosureClient).GetNestedType(
            "HouseFiling", BindingFlags.NonPublic);
        var houseFilingCtor = houseFilingType.GetConstructors()[0];

        var client = new HouseDisclosureClient(new HttpClient(), Substitute.For<ILogger<HouseDisclosureClient>>());
        var filing = houseFilingCtor.Invoke([
            "Jane Doe",
            "20251234",
            new DateOnly(2025, 2, 1),
            "CA01"
        ]);

        var lines = new[] { "SP APPLE INC (AAPL) P 01/14/2025 $1,001 - $15,000" };

        var result = (List<DisclosureTransaction>)parseTransactionLines.Invoke(client, [lines, filing]);

        result.Should().HaveCount(1);
        var tx = result[0];
        tx.MemberName.Should().Be("Jane Doe");
        tx.Position.Should().Be(CongressPosition.Representative);
        tx.Ticker.Should().Be("AAPL");
        tx.AssetName.Should().Be("APPLE INC (AAPL)");
        tx.TransactionDate.Should().Be(new DateOnly(2025, 1, 14));
        tx.FilingDate.Should().Be(new DateOnly(2025, 2, 1));
        tx.TransactionType.Should().Be(CongressTransactionType.Purchase);
        tx.OwnerType.Should().Be("SP");
        tx.AmountFrom.Should().Be(1001);
        tx.AmountTo.Should().Be(15000);
    }

    [Fact]
    public void RemoveTrailingTransactionType_SaleWithFullQualifier_StripsSuffixAndQualifier() {
        // Sibling to the existing "S (partial)" pin in RemoveTrailingTransactionType.
        // The strip path uses SaleTypeRegex `\bS\s*(\((?:partial|full)\))?\s*$` which
        // accepts BOTH qualifiers in the `(?:partial|full)` alternation. The
        // existing tests cover:
        //   - ExtractTransactionType for both partial AND full qualifiers
        //     (return-type detection — those pins read the regex via .IsMatch)
        //   - RemoveTrailingTransactionType for partial qualifier (.Replace path)
        // The `full` arm's BEHAVIOR through .Replace was unpinned.
        //
        // The risk this catches: a refactor that tightened the alternation to
        // just `partial` (a "we never see (full) in practice" simplification)
        // would compile, pass the ExtractTransactionType-full sibling
        // (different code path — `.IsMatch` vs `.Replace`), pass the
        // RemoveTrailingTransactionType-partial sibling, AND silently leave
        // every "S (full)" tail intact in persisted AssetName columns.
        // Downstream the asset name column would read "APPLE INC - COMMON
        // STOCK S (full)" — visible in the Congress trades UI and MCP tool
        // outputs.
        //
        // The two-step structure (recognize via IsMatch, then strip via
        // Replace) means dropping the `full` alternation has DIFFERENT
        // effects in each step:
        //   - IsMatch with `(?:partial)` only: "S (full)" no longer matches,
        //     ExtractTransactionType returns null, the row gets discarded
        //     in the upstream filter — caught by the existing sibling.
        //   - Replace with `(?:partial)` only: "S (full)" doesn't match the
        //     regex but the row already classified as Sale via PERHAPS a
        //     looser fallback path; Replace leaves the trailing tail in
        //     place. Pin THIS specific edge.
        //
        // Pin "S (full)" with a realistic asset string. Asserting the
        // cleanly-stripped output proves the full alternation arm in the
        // Replace pipeline still fires.
        var result = (string)RemoveTrailingTransactionTypeMethod.Invoke(null, ["GOOGLE LLC - CLASS A COMMON S (full)"]);

        result.Should().Be("GOOGLE LLC - CLASS A COMMON");
    }

    // ── Partial-branch fills ────────────────────────────────────────────

    [Fact]
    public void ExtractTransactionType_TextThatMatchesNeitherSaleNorPurchase_ReturnsNull() {
        // Pins the fall-through `return null` in ExtractTransactionType. Existing
        // pins cover only the Sale and Purchase return paths; neither evaluates
        // the second `if (PurchaseTypeRegex().IsMatch(text))` to false. Without
        // the fall-through, a regression that "tightens" PurchaseTypeRegex (or
        // re-orders the two ifs) could silently classify garbage text as a
        // Purchase by leaning on a default value.
        var result = (CongressTransactionType?)ExtractTransactionTypeMethod.Invoke(
            null, ["ACME WIDGETS COMMON STOCK"]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseTransactionLines_LineWithNoAssetOrTickerAfterStrip_SkipsSilently() {
        // Pins the both-empty short-circuit:
        //   if (string.IsNullOrEmpty(assetName) && string.IsNullOrEmpty(ticker)) continue;
        // Existing reflection pin has both populated, so the AND=true arm never
        // fires. Trigger here: a House PTR line with no asset description at all
        // — owner code + transaction type + date + amount. RemoveTrailingTransactionType
        // strips the trailing "P", assetName becomes empty, ticker stays null,
        // the continue fires, and no transaction is recorded. Without the
        // guard, the row would persist with both AssetName and Ticker empty.
        var parseTransactionLines = typeof(HouseDisclosureClient).GetMethod(
            "ParseTransactionLines", BindingFlags.NonPublic | BindingFlags.Instance);
        var houseFilingType = typeof(HouseDisclosureClient).GetNestedType(
            "HouseFiling", BindingFlags.NonPublic);
        var filing = houseFilingType.GetConstructors()[0].Invoke([
            "Jane Doe", "20251234", new DateOnly(2025, 2, 1), "CA01"
        ]);
        using var httpClient = new HttpClient();
        var client = new HouseDisclosureClient(httpClient, Substitute.For<ILogger<HouseDisclosureClient>>());
        var lines = new[] { "SP P 01/14/2025 $1,001 - $15,000" };

        var result = (List<DisclosureTransaction>)parseTransactionLines.Invoke(client, [lines, filing]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseTransactionLines_AssetWithoutParenthesizedTicker_RecordsTransactionWithNullTicker() {
        // Pins the `Ticker = ticker?.ToUpperInvariant()` null-conditional. The
        // null arm fires when the asset description carries no parenthesized
        // ticker — ExtractTickerFromAssetName returns null and the ticker
        // variable stays null all the way through to the property assignment.
        // The existing "realistic" pin always supplies "APPLE INC (AAPL)" so the
        // ticker is never null at row-construction time.
        var parseTransactionLines = typeof(HouseDisclosureClient).GetMethod(
            "ParseTransactionLines", BindingFlags.NonPublic | BindingFlags.Instance);
        var houseFilingType = typeof(HouseDisclosureClient).GetNestedType(
            "HouseFiling", BindingFlags.NonPublic);
        var filing = houseFilingType.GetConstructors()[0].Invoke([
            "Jane Doe", "20251234", new DateOnly(2025, 2, 1), "CA01"
        ]);
        using var httpClient = new HttpClient();
        var client = new HouseDisclosureClient(httpClient, Substitute.For<ILogger<HouseDisclosureClient>>());
        // No parens anywhere — ExtractTickerFromAssetName regex won't match.
        var lines = new[] { "SP APPLE INC COMMON STOCK P 01/14/2025 $1,001 - $15,000" };

        var result = (List<DisclosureTransaction>)parseTransactionLines.Invoke(client, [lines, filing]);

        result.Should().HaveCount(1);
        result[0].Ticker.Should().BeNull();
        result[0].AssetName.Should().Be("APPLE INC COMMON STOCK");
    }

    [Fact]
    public void ParsePtrPdf_MalformedPdfBytes_ReturnsEmptyListWithoutThrowing() {
        // ParsePtrPdf is the only callsite that wraps `PdfDocument.Open(pdfBytes)`
        // in a try/catch — Image #6 of the coverage report flags every line of the
        // try-body AND the catch-body as uncovered (lines 133-148). Until now this
        // file's tests only exercised the regex helpers; the defensive wrapper that
        // turns a single bad PDF into a per-filing skip (instead of crashing the
        // whole year's import loop in GetRecentTransactions) had zero coverage.
        //
        // Why the catch is load-bearing: DownloadAndParsePtrPdf is called per
        // filing inside `foreach (var filing in filings)`. If PdfPig throws on a
        // truncated CDN response or a malformed upload (the House Clerk has
        // shipped corrupted PTR PDFs in the past during their rollover windows),
        // the exception escapes back to GetRecentTransactions' outer
        // `catch (Exception)` — which is scoped to the YEAR, not the filing —
        // and the entire remainder of that year's filings get skipped with a
        // single "Failed to download House filing index" warning. The catch in
        // ParsePtrPdf scopes the failure to the offending filing only.
        //
        // A refactor that "simplifies" by removing the try/catch (under the
        // intuition that PdfPig handles all input gracefully, or that the
        // outer catch is "good enough") would compile, pass every regex pin
        // above, AND silently convert a one-filing-skip into a remaining-
        // year-loss the next time the Clerk ships a bad PDF. Pin the
        // defensive behavior here so that regression fails before it ships.
        //
        // The test passes obviously-not-PDF bytes (no `%PDF-` header) — PdfPig
        // throws inside `PdfDocument.Open`, the catch fires, the method
        // returns an empty list. The single assertion (empty + no throw)
        // proves the catch handled the exception; if the catch were removed,
        // the reflection Invoke call would surface a TargetInvocationException
        // and the test would fail.
        var parsePtrPdfMethod = typeof(HouseDisclosureClient).GetMethod(
            "ParsePtrPdf", BindingFlags.NonPublic | BindingFlags.Instance);
        var houseFilingType = typeof(HouseDisclosureClient).GetNestedType(
            "HouseFiling", BindingFlags.NonPublic);
        var houseFilingCtor = houseFilingType.GetConstructors()[0];
        var filing = houseFilingCtor.Invoke([
            "Jane Doe",
            "20251234",
            new DateOnly(2025, 2, 1),
            "CA01"
        ]);
        using var httpClient = new HttpClient();
        var client = new HouseDisclosureClient(httpClient, Substitute.For<ILogger<HouseDisclosureClient>>());
        // Bytes that are unambiguously not a PDF — PdfPig's PdfDocument.Open
        // requires the `%PDF-` magic header in the first ~1024 bytes.
        var malformedBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        var result = (List<DisclosureTransaction>)parsePtrPdfMethod.Invoke(client, [malformedBytes, filing]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentTransactions_FdZipContainsOneMatchingFilingButPtrPdfIs404_EntersPerFilingForeachAndExitsCleanly() {
        // Closes the last two coverlet branch-coverage partials in
        // HouseDisclosureClient — both inside DownloadAndParseFilingIndex /
        // GetRecentTransactions:
        //
        //   L42 — `foreach (var filing in filings) { ... }`. Only fires when
        //          filings is non-empty. The existing 404-status and missing-
        //          XML-entry pins leave filings empty (early-return before
        //          enumeration begins), so the foreach body has never executed.
        //   L82 — `if (xmlEntry == null) { ... return []; }`. The TRUE arm is
        //          covered by the wrong-entry-name pin; the FALSE arm requires
        //          archive.GetEntry($"{year}FD.xml") to find a real entry —
        //          structurally distinct from every existing fixture.
        //
        // Both branches are reachable only when the fixture stack delivers a
        // valid ZIP containing a `{year}FD.xml` whose XML body has at least one
        // <Member> with FilingType=P and FilingDate inside the request window.
        // Then DownloadAndParseFilingIndex emits one HouseFiling, the foreach
        // body runs, DownloadAndParsePtrPdf hits the PtR PDF URL → routed to
        // 404 by the test handler → returns []. The end-to-end result is an
        // empty transaction list, but two distinct URL paths were requested
        // and both coverage branches fired.
        //
        // A URL-routing handler is required (the existing BytesContentHandler
        // returns the SAME body for every request — that would feed the PDF
        // download the ZIP bytes and trigger PdfDocument.Open's exception
        // catch instead of the cleaner 404 short-circuit in
        // DownloadAndParsePtrPdf, leaving the PtR-URL-404 branch unexercised).
        const int year = 2025;
        const string docId = "20251234";
        // Minimal valid XML: XDocument.Descendants("Member") only requires a
        // single root element wrapping at least one <Member>. FilingDate format
        // is whatever DateOnly.TryParse accepts — MM/dd/yyyy is canonical
        // because that's what the production XML actually emits.
        var xml = $"""
            <FinancialDisclosures>
              <Member>
                <Prefix>Hon.</Prefix>
                <First>Jane</First>
                <Last>Doe</Last>
                <FilingType>P</FilingType>
                <StateDst>CA01</StateDst>
                <Year>{year}</Year>
                <FilingDate>2/1/{year}</FilingDate>
                <DocID>{docId}</DocID>
              </Member>
            </FinancialDisclosures>
            """;
        var zipBytes = BuildZipWithSingleEntry($"{year}FD.xml", xml);
        var handler = new UrlRoutingHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        var sut = new HouseDisclosureClient(httpClient, Substitute.For<ILogger<HouseDisclosureClient>>());

        var result = await sut.GetRecentTransactions(
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            CancellationToken.None);

        // Empty result confirms the PtR PDF 404 short-circuit fired (no
        // transactions parsed) AND the foreach actually iterated (otherwise
        // we'd have skipped the PDF request entirely).
        result.Should().BeEmpty();
        handler.Requests.Should().HaveCount(2,
            "one for the FD ZIP (with one matching filing inside) and one for the per-filing PtR PDF");
        handler.Requests[0].Should().Contain($"{year}FD.zip");
        handler.Requests[1].Should().Contain($"ptr-pdfs/{year}/{docId}.pdf");
    }

    private sealed class UrlRoutingHandler : HttpMessageHandler {
        private readonly byte[] _zipBytes;
        public List<string> Requests { get; } = new();

        public UrlRoutingHandler(byte[] zipBytes) => _zipBytes = zipBytes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);

            // FD ZIP — 200 + bytes
            if (url.Contains("/financial-pdfs/") && url.EndsWith("FD.zip")) {
                var response = new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new ByteArrayContent(_zipBytes)
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
                return Task.FromResult(response);
            }

            // PtR PDF — 404
            if (url.Contains("/ptr-pdfs/")) {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            // Any other URL is a test-setup error; surface a hard failure so
            // the test fails loudly rather than silently masquerading the bug.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
