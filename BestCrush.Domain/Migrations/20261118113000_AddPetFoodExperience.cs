using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BestCrush.Domain.Migrations;

[DbContext(typeof(BestCrushDbContext))]
[Migration("20261118113000_AddPetFoodExperience")]
public sealed class AddPetFoodExperience : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>("PetExperience","Resources",type:"REAL",nullable:true);
        migrationBuilder.CreateTable("PetFoods",columns:table=>new
        {
            DofusDbId=table.Column<long>(type:"INTEGER",nullable:false),
            Name=table.Column<string>(type:"TEXT",maxLength:256,nullable:false),
            DofusDbIconId=table.Column<long>(type:"INTEGER",nullable:true),
            Level=table.Column<int>(type:"INTEGER",nullable:false),
            MarketType=table.Column<int>(type:"INTEGER",nullable:false),
            Experience=table.Column<double>(type:"REAL",nullable:true),
            Craftable=table.Column<bool>(type:"INTEGER",nullable:false),
            ExperienceSource=table.Column<string>(type:"TEXT",maxLength:64,nullable:false),
            SheetExperience=table.Column<double>(type:"REAL",nullable:true)
        },constraints:table=>table.PrimaryKey("PK_PetFoods",x=>x.DofusDbId));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("PetFoods");
        migrationBuilder.Sql("ALTER TABLE Resources DROP COLUMN PetExperience;");
    }
}
