using Equibles.Data;

namespace Equibles.Messaging.Extensions;

public static class ModuleBuilderExtensions
{
    public static EquiblesModuleBuilder AddMessaging(this EquiblesModuleBuilder builder)
    {
        return builder.AddModule<MessagingModuleConfiguration>();
    }
}
