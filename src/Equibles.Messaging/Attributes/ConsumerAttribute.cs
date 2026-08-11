namespace Equibles.Messaging.Attributes;

// Marks an IConsumer<T> for auto-registration by AddMessaging's assembly scan.
// allowMultiple: when the same consumer type is registered from multiple
// assemblies, true => every instance gets the message (distinct endpoints);
// false => a single shared endpoint (round-robin, one handles it).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ConsumerAttribute : Attribute
{
    public bool AllowMultiple { get; }

    /// <summary>
    /// Exact durable receive-endpoint name. Set this when a consumer moves namespaces or hosts so
    /// the existing SQL transport queue remains the single canonical subscription.
    /// </summary>
    public string EndpointName { get; set; }

    public ConsumerAttribute(bool allowMultiple = false)
    {
        AllowMultiple = allowMultiple;
    }
}
