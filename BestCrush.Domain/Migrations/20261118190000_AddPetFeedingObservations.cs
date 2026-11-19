using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BestCrush.Domain.Migrations;

[DbContext(typeof(BestCrushDbContext))]
[Migration("20261118190000_AddPetFeedingObservations")]
public sealed class AddPetFeedingObservations:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("PetFeedingObservations",columns:t=>new
        {
            Id=t.Column<string>(type:"TEXT",maxLength:64,nullable:false),
            FoodItemId=t.Column<long>(type:"INTEGER",nullable:false),PetItemId=t.Column<long>(type:"INTEGER",nullable:false),
            Quantity=t.Column<int>(type:"INTEGER",nullable:false),ExperienceBefore=t.Column<long>(type:"INTEGER",nullable:false),
            ExperienceAfter=t.Column<long>(type:"INTEGER",nullable:false),ObservedAtUtc=t.Column<DateTime>(type:"TEXT",nullable:false),
            ClientBuild=t.Column<string>(type:"TEXT",maxLength:64,nullable:false)
        },constraints:t=>t.PrimaryKey("PK_PetFeedingObservations",x=>x.Id));
        migrationBuilder.CreateIndex("IX_PetFeedingObservations_FoodItemId_ClientBuild_ObservedAtUtc","PetFeedingObservations",
            ["FoodItemId","ClientBuild","ObservedAtUtc"]);
    }
    protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.DropTable("PetFeedingObservations");
}
