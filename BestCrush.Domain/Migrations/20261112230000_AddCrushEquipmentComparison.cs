using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BestCrush.Domain.Migrations;

[DbContext(typeof(BestCrushDbContext))]
[Migration("20261112230000_AddCrushEquipmentComparison")]
public sealed class AddCrushEquipmentComparison : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("ComparisonJson", "CrushHistoryEquipments", type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""ALTER TABLE "CrushHistoryEquipments" DROP COLUMN "ComparisonJson";""");
}
