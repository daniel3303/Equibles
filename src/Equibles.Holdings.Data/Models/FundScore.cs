using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>
/// The most recent performance score for an institutional filer: the hypothetical buy-and-hold
/// return of its reported 13F portfolio over a rolling window, measured against a benchmark.
/// One row per (holder, window length, benchmark) — recomputed in place, so this always holds
/// the latest score rather than a history. Alpha is the portfolio's annualised return (CAGR)
/// minus the benchmark's CAGR over the same simulated window.
/// </summary>
[Index(
    nameof(InstitutionalHolderId),
    nameof(WindowYears),
    nameof(BenchmarkTicker),
    IsUnique = true
)]
[Index(nameof(WindowYears), nameof(BenchmarkTicker), nameof(AlphaPercent))]
public class FundScore
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InstitutionalHolderId { get; set; }
    public virtual InstitutionalHolder InstitutionalHolder { get; set; }

    /// <summary>Benchmark the portfolio was measured against, e.g. "SPY".</summary>
    [MaxLength(16)]
    public string BenchmarkTicker { get; set; }

    /// <summary>Length of the rolling window in years, e.g. 3.</summary>
    public int WindowYears { get; set; }

    /// <summary>First day of the simulated window (the backtest's resolved start).</summary>
    public DateOnly WindowStart { get; set; }

    /// <summary>Last day of the simulated window (the backtest's resolved end).</summary>
    public DateOnly WindowEnd { get; set; }

    /// <summary>Total portfolio return over the window, as a percentage (e.g. 42.5 = +42.5%).</summary>
    [Precision(18, 4)]
    public decimal PortfolioTotalReturnPercent { get; set; }

    /// <summary>Annualised portfolio return (CAGR) over the window, as a percentage.</summary>
    [Precision(18, 4)]
    public decimal PortfolioCagrPercent { get; set; }

    /// <summary>Total benchmark return over the window, as a percentage.</summary>
    [Precision(18, 4)]
    public decimal BenchmarkTotalReturnPercent { get; set; }

    /// <summary>Annualised benchmark return (CAGR) over the window, as a percentage.</summary>
    [Precision(18, 4)]
    public decimal BenchmarkCagrPercent { get; set; }

    /// <summary>Portfolio CAGR minus benchmark CAGR — the alpha the ranking sorts on.</summary>
    [Precision(18, 4)]
    public decimal AlphaPercent { get; set; }

    /// <summary>Largest peak-to-trough portfolio decline over the window, as a percentage.</summary>
    [Precision(18, 4)]
    public decimal MaxDrawdownPercent { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;
}
