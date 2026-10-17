using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One Form ADV Item 5.D client type of a registered adviser from its latest roster: client
/// count and regulatory assets under management. Types the adviser reports neither clients nor
/// assets for are not stored.
/// </summary>
[Index(nameof(Crd), nameof(ClientType), IsUnique = true)]
public class FormAdvAdviserClientType
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Organization CRD number of the adviser.</summary>
    public int Crd { get; set; }

    public FormAdvClientType ClientType { get; set; }

    /// <summary>Number of clients (Item 5.D.(1)); null when not reported, including when fewer than 5 is checked instead.</summary>
    public int? ClientCount { get; set; }

    /// <summary>The adviser checked "fewer than 5 clients" (Item 5.D.(2)) instead of a count.</summary>
    public bool FewerThanFiveClients { get; set; }

    /// <summary>Regulatory assets under management for this client type (Item 5.D.(3)), in US dollars.</summary>
    public long? RegulatoryAum { get; set; }

    /// <summary>The roster snapshot this row was read from.</summary>
    public DateOnly ReportDate { get; set; }
}
