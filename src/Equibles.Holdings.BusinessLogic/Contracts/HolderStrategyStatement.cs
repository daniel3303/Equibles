namespace Equibles.Holdings.BusinessLogic.Contracts;

/// <summary>One stated strategy category and the verbatim sentence of the document that states it.</summary>
public sealed record HolderStrategyStatement(string Category, string Quote);
