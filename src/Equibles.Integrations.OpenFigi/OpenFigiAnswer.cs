using Newtonsoft.Json.Linq;

namespace Equibles.Integrations.OpenFigi;

// A job's instruments, or the error OpenFIGI refused that one job with; a refusal never decides its batch.
public sealed record OpenFigiAnswer(JArray Instruments, string Error);
