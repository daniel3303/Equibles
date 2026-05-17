using Equibles.Core.AutoWiring;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Errors.BusinessLogic;

[Service]
public class ErrorManager
{
    private readonly ErrorRepository _errorRepository;

    public ErrorManager(ErrorRepository errorRepository)
    {
        _errorRepository = errorRepository;
    }

    public async Task Create(
        ErrorSource source,
        string context,
        string message,
        string stackTrace,
        string requestSummary = null
    )
    {
        context ??= "Unknown";
        message ??= "No message provided";

        var error = new Error
        {
            Source = source,
            Context = Truncate(context, 128),
            Message = Truncate(message, 512),
            StackTrace = stackTrace,
            RequestSummary = Truncate(requestSummary, 512),
        };

        _errorRepository.Add(error);
        await _errorRepository.SaveChanges();
    }

    // Caps a value to maxLength UTF-16 units without splitting a surrogate
    // pair. Exception text routinely contains non-BMP chars (emoji); a raw
    // [..maxLength] slice could leave a dangling lone surrogate, which then
    // corrupts the text column and any JSON serialization for the dashboard.
    private static string Truncate(string value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
            return value;

        var end = char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength;
        return value[..end];
    }

    public async Task MarkAsSeen(Error error)
    {
        error.Seen = true;
        await _errorRepository.SaveChanges();
    }

    public async Task Delete(Error error)
    {
        _errorRepository.Delete(error);
        await _errorRepository.SaveChanges();
    }

    public async Task DeleteAll()
    {
        await _errorRepository.ExecuteDeleteAll();
    }
}
