using Equibles.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Messaging;

// Registers the MassTransit EF Core outbox/inbox entities into the shared
// EquiblesDbContext via the module system, so the transactional outbox is
// captured in the same DB as domain writes (no separate context).
public class MessagingModuleConfiguration : IModuleConfiguration
{
    public void ConfigureEntities(ModelBuilder builder)
    {
        builder.AddInboxStateEntity();
        builder.AddOutboxStateEntity();
        builder.AddOutboxMessageEntity();
    }
}
