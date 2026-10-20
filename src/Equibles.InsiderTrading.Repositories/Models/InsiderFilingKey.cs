namespace Equibles.InsiderTrading.Repositories.Models;

/// <summary>One insider's actual-share filing for an issuer, with the keys that order filings.</summary>
public class InsiderFilingKey
{
    public Guid InsiderOwnerId { get; set; }
    public string AccessionNumber { get; set; }
    public DateOnly TransactionDate { get; set; }
    public DateOnly FilingDate { get; set; }
}
