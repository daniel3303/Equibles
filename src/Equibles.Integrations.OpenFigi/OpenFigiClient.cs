using System.Text;
using Equibles.Integrations.Common.RateLimiter;
using Equibles.Integrations.Common.Retry;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Equibles.Integrations.OpenFigi;

// OpenFIGI's mapping API without a key: 25 requests a minute, 10 jobs each.
public sealed partial class OpenFigiClient(HttpClient http, ILogger<OpenFigiClient> logger)
{
    public static readonly Uri MappingUrl = new("https://api.openfigi.com/v3/mapping");
    public const int JobsPerRequest = 10;
    private const int RequestsPerMinute = 25;
    private const string NoIdentifier = "No identifier found.";
    private static readonly IRateLimiter SharedPace = new RateLimiter(
        1,
        TimeSpan.FromMinutes(1) / RequestsPerMinute
    );

    // Every client in the process shares the keyless allowance; tests replace it.
    public IRateLimiter Pace { get; init; } = SharedPace;

    // One answer per job, in job order: the instruments OpenFIGI maps it to (empty when it maps none), or its refusal.
    public async Task<IReadOnlyList<OpenFigiAnswer>> Map(
        IReadOnlyList<OpenFigiJob> jobs,
        CancellationToken token
    )
    {
        if (jobs.Count is 0 or > JobsPerRequest)
            throw new ArgumentException("An OpenFIGI request carries 1 to 10 jobs.", nameof(jobs));
        var body = new JArray(
            jobs.Select(job => new JObject
            {
                ["idType"] = job.IdType,
                ["idValue"] = job.IdValue,
                ["exchCode"] = job.ExchangeCode,
            })
        ).ToString(Formatting.None);
        using var response = await HttpRetry.Send(
            () =>
                http.PostAsync(
                    MappingUrl,
                    new StringContent(body, Encoding.UTF8, "application/json"),
                    token
                ),
            Pace,
            3,
            "OpenFIGI kept failing.",
            (_, delay) => LogThrottled(logger, delay),
            (status, _, delay) => LogServerError(logger, status, delay),
            token
        );
        JArray answers;
        try
        {
            answers = JArray.Parse(await response.Content.ReadAsStringAsync(token));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("OpenFIGI did not return a JSON array.", exception);
        }
        if (answers.Count != jobs.Count)
            throw new InvalidDataException("OpenFIGI answered a different number of jobs.");
        return answers.Select(Answer).ToList();
    }

    private static OpenFigiAnswer Answer(JToken answer)
    {
        if (answer is not JObject row)
            throw new InvalidDataException("OpenFIGI answered a job with a non-object.");
        if (row["data"] is JArray data && data.All(item => item is JObject))
            return new(data, null);
        if ((string)row["warning"] == NoIdentifier && row["data"] == null)
            return new([], null);
        return new(
            null,
            (string)row["error"] ?? (string)row["warning"] ?? "OpenFIGI gave no answer."
        );
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenFIGI throttled; retrying in {Delay}")]
    private static partial void LogThrottled(ILogger logger, TimeSpan delay);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "OpenFIGI returned {Status}; retrying in {Delay}"
    )]
    private static partial void LogServerError(ILogger logger, int status, TimeSpan delay);
}
