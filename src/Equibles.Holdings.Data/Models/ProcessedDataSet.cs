using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

[Index(nameof(FileName), IsUnique = true)]
public class ProcessedDataSet {
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(128)]
    public string FileName { get; set; }

    public int SubmissionCount { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
