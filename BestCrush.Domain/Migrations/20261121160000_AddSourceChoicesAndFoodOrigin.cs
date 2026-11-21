using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BestCrush.Domain.Migrations;

[DbContext(typeof(BestCrushDbContext))]
[Migration("20261121160000_AddSourceChoicesAndFoodOrigin")]
public sealed class AddSourceChoicesAndFoodOrigin:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("ExperienceOrigin","PetFoods",type:"INTEGER",nullable:true);
        migrationBuilder.Sql("UPDATE PetFoods SET ExperienceOrigin=CASE WHEN ExperienceObservedAtUtc IS NOT NULL THEN 1 ELSE 0 END WHERE Experience IS NOT NULL;");
        migrationBuilder.CreateTable("DataSourcePreferences",columns:t=>new
        {
            ServerName=t.Column<string>(type:"TEXT",maxLength:64,nullable:false),
            Kind=t.Column<int>(type:"INTEGER",nullable:false),ObjectType=t.Column<int>(type:"INTEGER",nullable:false),
            ItemId=t.Column<long>(type:"INTEGER",nullable:false),Quantity=t.Column<int>(type:"INTEGER",nullable:false),
            Source=t.Column<int>(type:"INTEGER",nullable:false)
        },constraints:t=>t.PrimaryKey("PK_DataSourcePreferences",p=>new{p.ServerName,p.Kind,p.ObjectType,p.ItemId,p.Quantity}));
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("DataSourcePreferences");
        migrationBuilder.Sql("ALTER TABLE PetFoods DROP COLUMN ExperienceOrigin;");
    }
}
