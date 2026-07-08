namespace Equibles.Errors.BusinessLogic.Extensions;

public static class ExceptionSummaryExtensions
{
    // The Errors dashboard lists rows by their Message, so a wrapper exception whose own
    // message is "An error occurred while saving the entity changes. See the inner exception
    // for details." (EF's DbUpdateException) or "One or more errors occurred." (AggregateException)
    // is undiagnosable at a glance — the real cause lives in an inner exception. Flatten the
    // InnerException chain into one line so the actual fault (e.g. the Postgres constraint
    // violation) shows in the list without expanding the full stack. The untruncated cause + all
    // inner stacks still go to the StackTrace column via Exception.ToString().
    public static string ToSummaryMessage(this Exception exception)
    {
        if (exception == null)
            return null;

        var messages = new List<string>();
        for (
            var current = exception;
            current != null && messages.Count < 5;
            current = current.InnerException
        )
        {
            var message = current.Message?.Trim();
            // Skip blanks and consecutive duplicates: a rethrow chain often repeats the same
            // message, which would just pad the line without adding signal.
            if (!string.IsNullOrEmpty(message) && !messages.Contains(message))
                messages.Add(message);
        }

        return messages.Count == 0 ? exception.Message : string.Join(" -> ", messages);
    }
}
