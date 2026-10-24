using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Equibles.Data;

// Base for string-valued smart enums stored as their Value. A derived type declares its singletons and
// registers them through Register(...) from a static field initializer; FromValue looks a value up
// case-insensitively and GetAll returns the singletons in declaration order.
//
// An EF value converter materializes a stored value through FromValue and falls back to `new T(value)`
// only when it returns null, so an unrecognised database value still round-trips unchanged.
public abstract class StringSmartEnum<T>
    where T : StringSmartEnum<T>
{
    private static readonly ConcurrentDictionary<string, T> ByValue = new(
        StringComparer.OrdinalIgnoreCase
    );
    private static T[] _declared = [];

    public string Value { get; }
    public string DisplayName { get; }

    protected StringSmartEnum(string value, string displayName = null)
    {
        Value = value;
        DisplayName = displayName ?? value;
    }

    // Registers the derived type's singletons and returns them; assign the result to a static field so
    // registration runs with the singleton initializers. A duplicate Value (case-insensitive) throws at
    // type initialization, so a copy-paste collision cannot silently shadow another value.
    protected static IReadOnlyCollection<T> Register(params T[] all)
    {
        foreach (var item in all)
        {
            if (!ByValue.TryAdd(item.Value, item))
            {
                throw new ArgumentException($"Duplicate {typeof(T).Name} value '{item.Value}'.");
            }
        }
        _declared = [.. _declared, .. all];
        return all;
    }

    public static T FromValue(string value)
    {
        if (value == null)
        {
            return null;
        }

        EnsureRegistered();
        return ByValue.GetValueOrDefault(value);
    }

    public static IReadOnlyCollection<T> GetAll()
    {
        EnsureRegistered();
        return Array.AsReadOnly(_declared);
    }

    public override string ToString() => Value;

    // FromValue and GetAll are inherited statics: calling T.FromValue runs only this base's initializer,
    // not T's, so T's Register(...) field initializer may not have run yet. Running T's type initializer
    // first populates the lookup (a no-op after the first call).
    private static void EnsureRegistered()
    {
        if (ByValue.IsEmpty)
        {
            RuntimeHelpers.RunClassConstructor(typeof(T).TypeHandle);
        }
    }
}
