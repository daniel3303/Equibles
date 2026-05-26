namespace Equibles.Web.ViewModels.Holdings;

public class HoldingsMostHeldViewModel
{
    public const int PageSize = 100;
    public const string SortFilers = "filers";
    public const string SortFilersDelta = "filersDelta";
    public const string SortValue = "value";

    public List<DateOnly> AvailableDates { get; set; } = [];
    public DateOnly SelectedDate { get; set; }
    public DateOnly? PreviousDate { get; set; }
    public bool IsCombinedAvailable { get; set; }
    public bool IsCombinedSelected { get; set; }

    public int TotalUniverseFilers { get; set; }

    // One of SortFilers / SortFilersDelta / SortValue. Normalised by the
    // controller before assignment so the view can render the selector
    // without re-validating.
    public string Sort { get; set; } = SortFilers;

    // 1-based page number; total rows + page count drive the pager footer.
    public int Page { get; set; } = 1;
    public int TotalRows { get; set; }
    public int TotalPages => Math.Max(1, (TotalRows + PageSize - 1) / PageSize);

    public List<HoldingsMostHeldRow> Rows { get; set; } = [];
}
