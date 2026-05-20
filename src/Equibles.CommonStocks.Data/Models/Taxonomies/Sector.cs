using System.ComponentModel.DataAnnotations;

namespace Equibles.CommonStocks.Data.Models.Taxonomies;

public class Sector
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public string Name { get; set; }
}
