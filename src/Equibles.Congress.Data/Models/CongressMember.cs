using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

[Index(nameof(Name), IsUnique = true)]
public class CongressMember
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Name { get; set; }

    public CongressPosition Position { get; set; }

    /// <summary>
    /// The seat the member holds, exactly as the House Clerk publishes it:
    /// a state postal code and a zero-padded district number ("SC05"), or the
    /// state alone for an at-large seat ("AK00"). Null for senators — no Senate
    /// filing states the member's state — and for members whose filings predate
    /// this being captured.
    /// </summary>
    [MaxLength(16)]
    public string StateDistrict { get; set; }

    public virtual List<CongressionalTrade> Trades { get; set; } = [];

    public virtual List<CongressionalAnnualDisclosure> AnnualDisclosures { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
