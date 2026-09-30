using Equibles.Migrations.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Equibles.Migrations.IntegrationTests;

[DbContext(typeof(HalfvecTestContext))]
[Migration("20260930150000_HalfvecIndexContract")]
public class HalfvecTestMigration : AddQwenHalfPrecisionEmbeddingIndex
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) { }
}
