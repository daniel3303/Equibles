using Equibles.Holdings.HostedService;

namespace Equibles.UnitTests.Holdings;

public class Holdings13FRealtimeWorkerParseDataSetEndDateNonDigitDayTests
{
    // Adds to the per-arm sweep of TryParseDatePart inside
    // Holdings13FRealtimeWorker.ParseDataSetEndDate. Existing pins cover:
    //   • ShortEndPart (length < 9 → null)
    //   • InvalidMonth (unrecognized 3-letter month → null)
    //   • YearZero (year < 1 → null)
    //   • InvalidDay (day > DaysInMonth → null)
    //   • NewFormat happy path
    //   • Old-format quarter-out-of-range
    // This pin covers the structurally distinct NON-DIGIT-DAY arm:
    //   if (!int.TryParse(part[..2], out var day)) return null;
    //
    // The two-character day prefix is the FIRST parse step in
    // TryParseDatePart. Real SEC filenames use 2-digit days (01..31),
    // but corrupted or hand-edited filenames can carry alphanumeric or
    // non-digit characters in the day slot — typically from a manual
    // mirror sync (e.g. `XXdec2025-YYjan2026_form13f.zip` from a
    // template that wasn't filled in, or from a wildcard-expanded
    // batch script that left placeholders).
    //
    // The risk this pin uniquely catches and that the other sibling
    // pins cannot:
    //   • Drop-the-int.TryParse — `var day = int.Parse(part[..2]);` —
    //     would compile, pass the existing siblings (each exercises
    //     a different arm), and throw FormatException on the first
    //     hand-edited filename with non-digit day characters. Each
    //     crash propagates up through the worker startup discovery
    //     loop and aborts the entire 13F realtime ingest cycle.
    //   • Swap-to-permissive — `int.TryParse(part[..2], NumberStyles.Any,
    //     ...)` — would compile, accept leading-space day strings
    //     (`" 1dec2025-...`") that don't match SEC's canonical 2-digit
    //     form. The day might still parse and downstream branches
    //     fire successfully, silently mis-attributing the dataset end
    //     date to the wrong day.
    //
    // The ShortEndPart sibling's input has < 9 chars and short-circuits
    // before the day parse. The InvalidDay sibling has VALID digits
    // ("31") that just exceed February's day count — exercises the
    // day-range guard, not the day-parse guard. Only a non-digit input
    // hits exclusively the int.TryParse fail-through.
    //
    // Pin: an end part starting with "XY" (non-digit) — must return
    // null without throwing.
    [Fact]
    public void ParseDataSetEndDate_NewFormatNonDigitDayPrefix_ReturnsNullWithoutThrowing()
    {
        var act = () =>
            Holdings13FRealtimeWorker.ParseDataSetEndDate("01dec2025-XYjan2026_form13f.zip");

        var result = act.Should().NotThrow().Subject;
        result.Should().BeNull();
    }
}
