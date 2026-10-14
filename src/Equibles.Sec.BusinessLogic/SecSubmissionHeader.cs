using System.Globalization;
using Equibles.Core.Calendars;

namespace Equibles.Sec.BusinessLogic;

// Reads facts EDGAR stamps in a full submission's SEC-HEADER, the authority for when a filing became public.
public static class SecSubmissionHeader
{
    // EDGAR writes ACCEPTANCE-DATETIME as an Eastern wall-clock yyyyMMddHHmmss; returns it in UTC, or
    // null when the header lacks a valid stamp. Only the header is read, so a quoted tag in a body never counts.
    public static DateTime? AcceptedAt(string submission)
    {
        var headerEnd =
            submission?.IndexOf("</SEC-HEADER>", StringComparison.OrdinalIgnoreCase) ?? -1;
        if (headerEnd < 0)
            return null;

        if (
            !SecSgmlEnvelope.TryGetTagValue(
                submission[..headerEnd],
                "ACCEPTANCE-DATETIME",
                out var stamp
            )
            || !DateTime.TryParseExact(
                stamp,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var eastern
            )
            || UsMarketCalendar.EasternTimeZone.IsInvalidTime(eastern)
        )
            return null;

        return TimeZoneInfo.ConvertTimeToUtc(eastern, UsMarketCalendar.EasternTimeZone);
    }
}
