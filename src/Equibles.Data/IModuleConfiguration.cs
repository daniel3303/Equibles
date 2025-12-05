using Microsoft.EntityFrameworkCore;

namespace Equibles.Data;

public interface IModuleConfiguration {
    void ConfigureEntities(ModelBuilder builder);
}
