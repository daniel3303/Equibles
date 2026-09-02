namespace Equibles.Holdings.HostedService.Services;

internal static class HoldingsBatchPacer
{
    // The flush task owns and disposes its repository scope before it completes. Waiting only
    // after that completion prevents throttling from holding a scarce database connection idle.
    internal static async Task<T> Complete<T>(
        Task<T> flushTask,
        Func<T, bool> shouldPause,
        TimeSpan pause,
        CancellationToken cancellationToken
    )
    {
        var result = await flushTask;
        if (shouldPause(result) && pause > TimeSpan.Zero)
        {
            await Task.Delay(pause, cancellationToken);
        }

        return result;
    }
}
