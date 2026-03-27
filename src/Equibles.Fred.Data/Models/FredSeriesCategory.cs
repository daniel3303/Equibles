using System.ComponentModel.DataAnnotations;

namespace Equibles.Fred.Data.Models;

public enum FredSeriesCategory {
    [Display(Name = "Interest Rates")]
    InterestRates,

    [Display(Name = "Yield Spreads")]
    YieldSpreads,

    [Display(Name = "Corporate Bond Spreads")]
    CorporateBondSpreads,

    [Display(Name = "Inflation")]
    Inflation,

    [Display(Name = "Employment")]
    Employment,

    [Display(Name = "GDP & Output")]
    GdpAndOutput,

    [Display(Name = "Money Supply")]
    MoneySupply,

    [Display(Name = "Sentiment")]
    Sentiment,

    [Display(Name = "Housing")]
    Housing,

    [Display(Name = "Exchange Rates")]
    ExchangeRates,

    [Display(Name = "Market")]
    Market
}
