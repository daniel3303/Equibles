namespace Equibles.Mcp;

// Lets a host see the structured data behind one tool answer, which the text alone cannot carry
// (an interactive card beside the answer, for instance). A tool records the rows it rendered
// after its last early return, so an answer that failed or found nothing records nothing.
//
// The host opens a scope before it dispatches the call and reads it afterwards. The scope is a
// mutable holder because a value assigned to an AsyncLocal inside the awaited tool never flows
// back to the caller; adding to the holder the caller created does. With no scope open (other
// hosts, the agent's in-process calls) Record is a no-op.
public static class McpResultCapture
{
    private static readonly AsyncLocal<Scope> Ambient = new();

    public static Scope Begin()
    {
        var scope = new Scope(Ambient.Value);
        Ambient.Value = scope;
        return scope;
    }

    public static void Record(object result)
    {
        if (result != null)
            Ambient.Value?.Add(result);
    }

    public sealed class Scope : IDisposable
    {
        private readonly Scope _previous;
        private readonly List<object> _results = [];
        private readonly object _gate = new();

        internal Scope(Scope previous) => _previous = previous;

        public IReadOnlyList<object> Results
        {
            get
            {
                lock (_gate)
                    return _results.ToList();
            }
        }

        // The last result of the given type, or null when the tool recorded none.
        public T Get<T>()
            where T : class
        {
            lock (_gate)
                return _results.OfType<T>().LastOrDefault();
        }

        internal void Add(object result)
        {
            lock (_gate)
                _results.Add(result);
        }

        public void Dispose() => Ambient.Value = _previous;
    }
}
