using System.ComponentModel.DataAnnotations;

namespace Equibles.Fred.Data.Models;

// The publisher an indicator series is read from. FRED series keep FRED's own id; the others carry a
// publisher prefix, so one public id never names two series.
public enum IndicatorSource
{
    [Display(Name = "FRED")]
    Fred,

    [Display(Name = "Bank of Canada")]
    BankOfCanada,

    [Display(Name = "Statistics Canada")]
    StatisticsCanada,
}
