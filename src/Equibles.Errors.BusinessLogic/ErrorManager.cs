using Equibles.Core.AutoWiring;
using Equibles.Errors.Data.Models;
using Equibles.Errors.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Errors.BusinessLogic;

[Service]
public class ErrorManager {
    private readonly ErrorRepository _errorRepository;

    public ErrorManager(ErrorRepository errorRepository) {
        _errorRepository = errorRepository;
    }

    public async Task Create(ErrorSource source, string context, string message, string stackTrace,
        string requestSummary = null) {
        context ??= "Unknown";
        message ??= "No message provided";

        var error = new Error {
            Source = source,
            Context = context.Length > 128 ? context[..128] : context,
            Message = message.Length > 512 ? message[..512] : message,
            StackTrace = stackTrace,
            RequestSummary = requestSummary?.Length > 512 ? requestSummary[..512] : requestSummary
        };

        _errorRepository.Add(error);
        await _errorRepository.SaveChanges();
    }

    public async Task MarkAsSeen(Error error) {
        error.Seen = true;
        await _errorRepository.SaveChanges();
    }

    public async Task Delete(Error error) {
        _errorRepository.Delete(error);
        await _errorRepository.SaveChanges();
    }

    public async Task DeleteAll() {
        await _errorRepository.GetDbSet().ExecuteDeleteAsync();
    }
}
