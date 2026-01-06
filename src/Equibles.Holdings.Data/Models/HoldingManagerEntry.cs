using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Owned]
public class HoldingManagerEntry {
    public int? ManagerNumber { get; set; }

    [MaxLength(256)]
    public string ManagerName { get; set; }

    public long Shares { get; set; }
    public long Value { get; set; }
    public InvestmentDiscretion InvestmentDiscretion { get; set; }
}
