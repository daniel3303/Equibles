namespace Equibles.DelayedTrades.BusinessLogic.Configuration;

// Section `DelayedTradeScraper`; every interval is in the market's own local time unless it says UTC.
public class DelayedTradeScraperOptions
{
    /// <summary>How often the worker checks every market for due work.</summary>
    public int ControlIntervalMinutes { get; set; } = 1;

    /// <summary>Cadence of the current-session poll inside the intraday window.</summary>
    public int IntradayPollMinutes { get; set; } = 15;

    /// <summary>The intraday window opens this long before the session opens.</summary>
    public int PreOpenMinutes { get; set; } = 5;

    /// <summary>The intraday window closes this long after the closing auction ends.</summary>
    public int PostAuctionMinutes { get; set; } = 30;

    /// <summary>Local time from which the previous session's file is expected to carry the last session.</summary>
    public TimeOnly SettleWindowStart { get; set; } = new(0, 30);

    /// <summary>Cadence of settle attempts while the last completed session is not yet marked.</summary>
    public int SettlePollMinutes { get; set; } = 60;

    /// <summary>The daily re-derivation of the previous session runs this long after the open; zero disables it.</summary>
    public int RecheckMinutesAfterOpen { get; set; } = 180;

    /// <summary>A print published closer to the fetch than this is dropped before any figure is derived.</summary>
    public int PublicationDelayMinutes { get; set; } = 15;

    /// <summary>Capture ledger rows older than this are pruned.</summary>
    public int CaptureRetentionDays { get; set; } = 90;

    /// <summary>Count a repeated trade identifier once; off by default because it holds every identifier of a session in memory.</summary>
    public bool DetectDuplicateTradeIds { get; set; }
}
