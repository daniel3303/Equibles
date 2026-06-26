using Equibles.Web.ViewModels.Shared;

namespace Equibles.Web.ViewModels.ShortVolume;

public class ShortVolumeBrowserViewModel : PagedBrowserViewModel
{
    public List<ShortVolumeListItemViewModel> Records { get; set; } = [];

    // The trading day being shown (the parsed selection, or the latest available).
    public DateOnly? SelectedDate { get; set; }

    // The most recent trading day on file — drives the default selection and the subtitle.
    public DateOnly? LatestDate { get; set; }

    // Distinct trading days with short-volume data, newest first — populates the day selector.
    public List<DateOnly> AvailableDates { get; set; } = [];

    public ShortVolumeSort Sort { get; set; }
}
