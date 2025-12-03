using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Equibles.Core.Extensions;

public static class EnumExtensions {
    public static string NameForHumans(this Enum enumValue) {
        return enumValue.GetType()
            .GetMember(enumValue.ToString())
            .First()
            .GetCustomAttribute<DisplayAttribute>()
            ?.GetName() ?? enumValue.ToString();
    }
}