using Microsoft.EntityFrameworkCore;

namespace Equibles.Migrations.IntegrationTests;

public class HalfvecTestContext(DbContextOptions<HalfvecTestContext> options) : DbContext(options);
