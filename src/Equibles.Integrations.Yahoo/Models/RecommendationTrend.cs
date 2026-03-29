namespace Equibles.Integrations.Yahoo.Models;

public class RecommendationTrend {
    public string Period { get; set; }
    public int StrongBuy { get; set; }
    public int Buy { get; set; }
    public int Hold { get; set; }
    public int Sell { get; set; }
    public int StrongSell { get; set; }
}
