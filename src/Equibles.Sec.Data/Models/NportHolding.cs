using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// A single portfolio investment reported on a <see cref="NportFiling"/> — one line of the fund's
/// schedule of investments. Captures the issuer/instrument name and identifiers, the position size
/// and its U.S.-dollar value, the share of the fund's net assets it represents, and the asset and
/// issuer categories NPORT reports as short codes (e.g. asset "EC" equity-common, "DBT" debt,
/// "DE" derivative; issuer "CORP" corporate, "RF" registered fund).
/// </summary>
[Index(nameof(NportFilingId))]
[Index(nameof(Cusip))]
public class NportHolding
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NportFilingId { get; set; }
    public virtual NportFiling NportFiling { get; set; }

    /// <summary>The issuer or instrument name exactly as reported.</summary>
    [MaxLength(512)]
    public string Name { get; set; }

    /// <summary>The title of the issue, e.g. the security description.</summary>
    [MaxLength(512)]
    public string Title { get; set; }

    /// <summary>The holding's CUSIP when reported (the literal "N/A" is stored as null).</summary>
    [MaxLength(16)]
    public string Cusip { get; set; }

    /// <summary>The holding's ISIN when reported.</summary>
    [MaxLength(32)]
    public string Isin { get; set; }

    /// <summary>The issuer's Legal Entity Identifier when reported.</summary>
    [MaxLength(32)]
    public string Lei { get; set; }

    /// <summary>The position size in the reported units (shares, principal amount or contracts).</summary>
    public decimal Balance { get; set; }

    /// <summary>The unit the balance is expressed in: "NS" shares, "PA" principal amount, "NC" contracts.</summary>
    [MaxLength(16)]
    public string Units { get; set; }

    /// <summary>The currency the holding is denominated in, e.g. "USD".</summary>
    [MaxLength(8)]
    public string Currency { get; set; }

    /// <summary>The holding's value in U.S. dollars.</summary>
    public decimal ValueUsd { get; set; }

    /// <summary>The holding's share of the fund's net assets, as a percentage (can be negative for shorts).</summary>
    public decimal PercentValue { get; set; }

    /// <summary>The payoff profile, "Long" or "Short".</summary>
    [MaxLength(16)]
    public string PayoffProfile { get; set; }

    /// <summary>The NPORT asset-category code, e.g. "EC" (equity-common), "DBT" (debt), "DE" (derivative).</summary>
    [MaxLength(16)]
    public string AssetCategory { get; set; }

    /// <summary>The NPORT issuer-category code, e.g. "CORP" (corporate), "RF" (registered fund).</summary>
    [MaxLength(16)]
    public string IssuerCategory { get; set; }

    /// <summary>The investment's country code, e.g. "US".</summary>
    [MaxLength(8)]
    public string InvestmentCountry { get; set; }
}
