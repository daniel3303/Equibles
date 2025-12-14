using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Errors.Data.Models;

[Table("Errors")]
[Index(nameof(Seen))]
[Index(nameof(CreationTime))]
[Index(nameof(Source))]
public class Error {
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public ErrorSource Source { get; set; }

    [Required]
    [MaxLength(128)]
    public string Context { get; set; }

    [Required]
    [MaxLength(512)]
    public string Message { get; set; }

    public string StackTrace { get; set; }

    [MaxLength(512)]
    public string RequestSummary { get; set; }

    public bool Seen { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
