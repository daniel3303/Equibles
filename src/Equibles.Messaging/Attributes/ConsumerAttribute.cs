namespace Equibles.Messaging.Attributes;

// Marks an IConsumer<T> for auto-registration by AddMessaging's assembly scan.
// allowMultiple: when the same consumer type is registered from multiple
// assemblies, true => every instance gets the message (distinct endpoints);
// false => a single shared endpoint (round-robin, one handles it).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ConsumerAttribute : Attribute
{
    public bool AllowMultiple { get; }

    public ConsumerAttribute(bool allowMultiple = false)
    {
        AllowMultiple = allowMultiple;
    }
}
