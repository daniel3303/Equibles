using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Congress.Data.Models;

[Index(nameof(Name), IsUnique = true)]
public class CongressMember {
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string Name { get; set; }

    public CongressPosition Position { get; set; }

    public virtual List<CongressionalTrade> Trades { get; set; } = [];

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
