namespace Equibles.Core.Exceptions;

/// <summary>
/// Thrown when a domain entity fails business validation rules.
/// Callers can catch this to distinguish validation errors from system errors.
/// </summary>
public class DomainValidationException : Exception {
    public DomainValidationException(string message) : base(message) { }
}
