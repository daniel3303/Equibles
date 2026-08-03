using Equibles.Worker;

namespace Equibles.Finra.HostedService.Configuration;

public class FinraScraperOptions : ScraperOptions
{
    /// <summary>
    /// Maximum previously-unfinished daily short-volume files to reconcile per worker cycle.
    /// Recent files are visited first, so live coverage stays current while history fills in.
    /// </summary>
    public int ShortVolumeBackfillDatesPerCycle { get; set; } = 100;

    /// <summary>
    /// When true (default), the worker minute-polls for the daily short-volume file during
    /// the post-close ET window on NYSE trading days so it syncs the moment FINRA publishes
    /// it, instead of waiting the full <see cref="ScraperOptions.SleepIntervalHours"/> cycle.
    /// Set false to fall back to the plain fixed-interval cycle.
    /// </summary>
    public bool EveningPollEnabled { get; set; } = true;

    /// <summary>
    /// When true (default), the worker also minute-polls for the semi-monthly short INTEREST
    /// file during the same post-close window, on the ~24 days a year FINRA's reporting
    /// calendar publishes one (see <c>ShortInterestCalendar</c>). Polling stops as soon as that
    /// cycle's settlement date is stored. Requires <see cref="EveningPollEnabled"/>.
    /// </summary>
    public bool ShortInterestPublicationPollEnabled { get; set; } = true;

    /// <summary>
    /// How often to re-check for a file while inside the post-close poll window. Governs both
    /// the daily short-volume poll and the publication-day short-interest poll.
    /// </summary>
    public int ShortVolumePollIntervalMinutes { get; set; } = 1;

    /// <summary>Poll-window start, ET hour (16 = 16:00, regular market close).</summary>
    public int WindowStartHourEt { get; set; } = 16;

    /// <summary>
    /// Poll-window end, ET hour (22 = 22:00). If the file still hasn't published by this
    /// hour the minute-poll stops for the day; the next idle cycle's import still backfills it.
    /// </summary>
    public int WindowEndHourEt { get; set; } = 22;
}
