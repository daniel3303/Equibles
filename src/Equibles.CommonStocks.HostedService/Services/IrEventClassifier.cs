using Equibles.CommonStocks.Data.Models;

namespace Equibles.CommonStocks.HostedService.Services;

/// <summary>
/// Maps an IR event's own label to a normalised kind. Unknown when no label
/// matches — never a guess. Shared by every IR platform parser so the same label
/// always classifies the same way regardless of which platform published it.
/// </summary>
public static class IrEventClassifier
{
    public static IrEventType Classify(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("earnings"))
            return IrEventType.EarningsCall;
        if (t.Contains("annual meeting") || t.Contains("shareholder") || t.Contains("stockholder"))
            return IrEventType.ShareholderMeeting;
        if (t.Contains("conference"))
            return IrEventType.Conference;
        if (t.Contains("investor day") || t.Contains("analyst day") || t.Contains("presentation"))
            return IrEventType.Presentation;
        if (t.Contains("webcast"))
            return IrEventType.Webcast;
        return IrEventType.Unknown;
    }
}
