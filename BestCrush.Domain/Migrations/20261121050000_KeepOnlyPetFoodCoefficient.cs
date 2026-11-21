using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BestCrush.Domain.Migrations;

[DbContext(typeof(BestCrushDbContext))]
[Migration("20261121050000_KeepOnlyPetFoodCoefficient")]
public sealed class KeepOnlyPetFoodCoefficient:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("ExperienceObservedAtUtc","PetFoods",type:"TEXT",nullable:true);
        // Convert the latest confirmed meal once, then remove the raw history.
        migrationBuilder.Sql("""
            WITH latest AS (
                SELECT FoodItemId,
                    CAST(ExperienceAfter-ExperienceBefore AS REAL)/Quantity AS Coefficient,
                    ObservedAtUtc,
                    ROW_NUMBER() OVER (PARTITION BY FoodItemId ORDER BY ObservedAtUtc DESC, Id DESC) AS Position
                FROM PetFeedingObservations
                WHERE Quantity>0 AND ExperienceBefore>=0 AND ExperienceAfter>=ExperienceBefore AND ExperienceAfter<179592
            )
            UPDATE PetFoods SET
                Experience=(SELECT Coefficient FROM latest WHERE FoodItemId=PetFoods.DofusDbId AND Position=1),
                ExperienceObservedAtUtc=(SELECT ObservedAtUtc FROM latest WHERE FoodItemId=PetFoods.DofusDbId AND Position=1),
                ExperienceSource='Relevé en jeu'
            WHERE DofusDbId IN (SELECT FoodItemId FROM latest WHERE Position=1);
            UPDATE Resources SET PetExperience=(SELECT Experience FROM PetFoods WHERE DofusDbId=Resources.DofusDbId)
            WHERE DofusDbId IN (SELECT DofusDbId FROM PetFoods WHERE ExperienceObservedAtUtc IS NOT NULL AND MarketType=1);
            """);
        migrationBuilder.DropTable("PetFeedingObservations");
        migrationBuilder.Sql("ALTER TABLE PetFoods DROP COLUMN SheetExperience;");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE PetFoods DROP COLUMN ExperienceObservedAtUtc;");
        migrationBuilder.AddColumn<double>("SheetExperience","PetFoods",type:"REAL",nullable:true);
        // A rollback recreates the old empty schema; deleted meals are not reconstructed.
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
}
