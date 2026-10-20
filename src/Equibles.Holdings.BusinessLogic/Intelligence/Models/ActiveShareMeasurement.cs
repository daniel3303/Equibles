namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>An active share and the benchmark weight held in the portfolio's names, each 0 to 1.</summary>
public readonly record struct ActiveShareMeasurement(decimal ActiveShare, decimal Overlap);
