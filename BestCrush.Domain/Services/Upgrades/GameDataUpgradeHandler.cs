using BestCrush.Domain.Models;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Models.Characteristics;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using DofusSharp.DofusDb.ApiClients.Models.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProgressMessage = BestCrush.Domain.Models.ProgressMessage;

namespace BestCrush.Domain.Services.Upgrades;

public partial class GameDataUpgradeHandler(
    BestCrushDbContext dbContext,
    IDofusDbQueryProvider dofusDbQueryProvider,
    ILogger<GameDataUpgradeHandler> logger
)
{
    private const string
        RuneCatalogUpgradeVersion =
            "dofusdb-base-effects-v3";

    public async Task UpgradeAsync(
        Version newVersion,
        ProgressSync<ProgressMessage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Upgrade? lastUpgrade =
            await dbContext.Upgrades
                .Where(upgrade =>
                    upgrade.Kind ==
                    UpgradeKind.DofusDb)
                .OrderByDescending(upgrade =>
                    upgrade.UpgradeDate)
                .FirstOrDefaultAsync(
                    cancellationToken
                );

        Version? oldVersion =
            Version.TryParse(
                lastUpgrade?.NewVersion,
                out Version? version
            )
                ? version
                : null;

        bool runeCatalogUpgradeApplied =
            await dbContext.Upgrades
                .AnyAsync(
                    upgrade =>
                        upgrade.Kind ==
                            UpgradeKind.RuneCatalog &&
                        upgrade.NewVersion ==
                            RuneCatalogUpgradeVersion,
                    cancellationToken
                );


        bool hasLegacyEquipmentIngredients =
            await (
                from resource in dbContext.Resources
                join equipment in dbContext.Equipments
                    on resource.DofusDbId equals equipment.DofusDbId
                select resource.Id
            )
            .AnyAsync(
                cancellationToken
            );

        // Avant cette évolution, tous les ingrédients de recette
        // étaient créés comme Resource, y compris les équipements.
        // Un DofusDbId présent à la fois dans Resources et Equipments
        // signale donc l'ancien catalogue et déclenche une reconstruction
        // complète des seules données de jeu.
        if (oldVersion == newVersion &&
            hasLegacyEquipmentIngredients)
        {
            logger.LogInformation(
                "Migrating recipe ingredients: equipment ingredients will no longer be stored as resources."
            );

            progress?.Report(
                "Mise à jour des ingrédients de recettes."
            );

            await RebuildGameDataAsync(
                progress,
                cancellationToken
            );

            progress?.Report(
                "Les ingrédients de recettes ont été mis à jour.",
                100,
                true
            );

            return;
        }

        if (oldVersion == newVersion)
        {
            bool missingEquipmentStats = await dbContext.Equipments.AnyAsync(cancellationToken) &&
                !await dbContext.Set<ItemCharacteristicLine>().AnyAsync(cancellationToken);
            if (missingEquipmentStats)
            {
                progress?.Report("Réparation des caractéristiques des équipements.");
                var statsProgress = runeCatalogUpgradeApplied ? progress : progress?.DeriveSubtask(0, 80);
                await RestoreEquipmentCharacteristicsAsync(statsProgress, cancellationToken);
            }

            if (!runeCatalogUpgradeApplied)
            {
                progress?.Report("Mise à jour du catalogue des runes.");
                var runeProgress = missingEquipmentStats ? progress?.DeriveSubtask(80, 100) : progress;
                await RebuildRuneCatalogAsync(runeProgress, cancellationToken);
            }

            if (missingEquipmentStats || !runeCatalogUpgradeApplied)
                progress?.Report("Les données du jeu ont été mises à jour.", 100, true);
            else
                logger.LogInformation("DofusDB data is up to date. Version: {Version}.", newVersion);
            return;
        }

        logger.LogInformation(
            "Running upgrade from version {OldVersion} to {NewVersion}...",
            oldVersion,
            newVersion
        );

        progress?.Report(
            "Mise à jour des données du jeu."
        );

        await RebuildGameDataAsync(
            progress,
            cancellationToken
        );

        Upgrade newUpgrade =
            new()
            {
                Kind =
                    UpgradeKind.DofusDb,
                OldVersion =
                    oldVersion?.ToString(),
                NewVersion =
                    newVersion.ToString(),
                UpgradeDate =
                    DateTime.Now
            };

        dbContext.Upgrades.Add(
            newUpgrade
        );

        await dbContext.SaveChangesAsync(
            cancellationToken
        );

        progress?.Report(
            "Les données du jeu ont été mises à jour.",
            100,
            true
        );

        logger.LogInformation(
            "Successfully upgraded DofusDB data to version {NewVersion}.",
            newVersion
        );
    }

    private async Task RebuildGameDataAsync(
        ProgressSync<ProgressMessage>? progress,
        CancellationToken cancellationToken)
    {
        (
            Dictionary<long, DofusDbCharacteristic>
                characteristicsDict,
            Dictionary<long, DofusDbRecipe>
                recipesDict,
            DofusDbItem[] equipments,
            DofusDbItem[] ingredients
        ) =
            await FetchDataAsync(progress?.DeriveSubtask(0, 40), cancellationToken);

        if (equipments.Any(item => item.PossibleEffects?.Any(effect => effect.BaseEffectId is > 0) == true) &&
            !equipments.Any(item => ReadCharacteristicRanges(item, characteristicsDict).Any()) &&
            await dbContext.Set<ItemCharacteristicLine>().AnyAsync(cancellationToken))
            throw new InvalidDataException("Le catalogue reçu ne contient aucune caractéristique d’équipement reconnue.");

        await ClearTables(progress?.DeriveSubtask(40, 50), cancellationToken);

        HashSet<long> equipmentIds =
            equipments
                .Where(equipment =>
                    equipment.Id.HasValue)
                .Select(equipment =>
                    equipment.Id!.Value)
                .ToHashSet();

        CreateIngredients(
            ingredients,
            equipmentIds,
            progress?.DeriveSubtask(
                50,
                60
            )
        );

        await dbContext.SaveChangesAsync(
            cancellationToken
        );

        await CreateEquipmentsAsync(
            characteristicsDict,
            equipments,
            progress?.DeriveSubtask(
                60,
                75
            ),
            cancellationToken
        );

        await dbContext.SaveChangesAsync(
            cancellationToken
        );

        await CreateRecipesAsync(
            recipesDict,
            equipments,
            progress?.DeriveSubtask(
                75,
                85
            ),
            cancellationToken
        );

        await dbContext.SaveChangesAsync(
            cancellationToken
        );

        await RefreshRunesAsync(
            characteristicsDict,
            progress?.DeriveSubtask(
                85,
                100
            ),
            cancellationToken
        );
    }

    private async Task RebuildRuneCatalogAsync(
        ProgressSync<ProgressMessage>? progress,
        CancellationToken cancellationToken)
    {
        IDofusDbQuery<DofusDbCharacteristic>
            characteristicsQuery =
                dofusDbQueryProvider
                    .Characteristics();

        DofusDbCharacteristic[]
            characteristics =
                await characteristicsQuery
                    .ExecuteAsync(
                        progress?
                            .DeriveSubtask(
                                0,
                                35
                            )
                            .ToMultiSearchProgress(
                                "Récupération des caractéristiques"
                            ),
                        cancellationToken
                    )
                    .ToArrayAsync(
                        cancellationToken
                    );

        Dictionary<
            long,
            DofusDbCharacteristic>
            characteristicsDict =
                characteristics
                    .Where(characteristic =>
                        characteristic.Id
                            .HasValue)
                    .ToDictionary(
                        characteristic =>
                            characteristic.Id!
                                .Value,
                        characteristic =>
                            characteristic
                    );

        await RefreshRunesAsync(
            characteristicsDict,
            progress?.DeriveSubtask(
                35,
                95
            ),
            cancellationToken
        );
    }

    private async Task
        EnsureRuneCatalogUpgradeMarkerAsync(
            CancellationToken cancellationToken)
    {
        bool alreadyApplied =
            await dbContext.Upgrades
                .AnyAsync(
                    upgrade =>
                        upgrade.Kind ==
                            UpgradeKind.RuneCatalog &&
                        upgrade.NewVersion ==
                            RuneCatalogUpgradeVersion,
                    cancellationToken
                );

        if (alreadyApplied)
        {
            return;
        }

        dbContext.Upgrades.Add(
            new Upgrade
            {
                Kind =
                    UpgradeKind.RuneCatalog,
                OldVersion =
                    null,
                NewVersion =
                    RuneCatalogUpgradeVersion,
                UpgradeDate =
                    DateTime.Now
            }
        );
    }

    async Task ClearTables(ProgressSync<ProgressMessage>? progress, CancellationToken cancellationToken)
    {
        progress?.ReportStep("Suppression des anciennes données", 1, 6);
        await ClearTableAsync<ItemCharacteristicLine>(cancellationToken);
        progress?.ReportStep("Suppression des anciennes données", 2, 6);
        await ClearTableAsync<RecipeEntry>(cancellationToken);
        progress?.ReportStep("Suppression des anciennes données", 3, 6);
        await ClearTableAsync<EquipmentRecipeEntry>(cancellationToken);
        progress?.ReportStep("Suppression des anciennes données", 4, 6);
        await ClearTableAsync<Resource>(cancellationToken);
        progress?.ReportStep("Suppression des anciennes données", 5, 6);
        await ClearTableAsync<Equipment>(cancellationToken);
        progress?.ReportStep("Suppression des anciennes données", 6, 6);
    }

    async Task ClearTableAsync<T>(CancellationToken cancellationToken = default)
    {
        string? tableName = dbContext.Model.FindEntityType(typeof(T))?.GetTableName();
        if (tableName is null)
        {
            throw new InvalidOperationException($"Could not find table name for entity type {typeof(T).FullName}");
        }

#pragma warning disable EF1002
        await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName};", cancellationToken);
#pragma warning restore EF1002
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    async Task<(Dictionary<long, DofusDbCharacteristic> characteristicsDict, Dictionary<long, DofusDbRecipe> recipesDict, DofusDbItem[] equipments, DofusDbItem[] ingredients)>
        FetchDataAsync(ProgressSync<ProgressMessage>? progress = null, CancellationToken cancellationToken = default)
    {
        IDofusDbQuery<DofusDbCharacteristic> characteristicsQuery = dofusDbQueryProvider.Characteristics();
        DofusDbCharacteristic[] characteristics = await characteristicsQuery
            .ExecuteAsync(progress?.DeriveSubtask(0, 20).ToMultiSearchProgress("Récupération des caractéristiques"), cancellationToken)
            .ToArrayAsync(cancellationToken);
        Dictionary<long, DofusDbCharacteristic> characteristicsDict = characteristics.Where(c => c.Id.HasValue).ToDictionary(c => c.Id!.Value, c => c);

        IDofusDbQuery<DofusDbRecipe> recipesQuery = dofusDbQueryProvider.Recipes();
        DofusDbRecipe[] recipes = await recipesQuery
            .ExecuteAsync(progress?.DeriveSubtask(20, 60).ToMultiSearchProgress("Récupération des recettes"), cancellationToken)
            .ToArrayAsync(cancellationToken);
        Dictionary<long, DofusDbRecipe> recipesDict = recipes.Where(r => r.ResultId.HasValue).ToDictionary(c => c.ResultId!.Value, c => c);

        DofusDbItem[] equipments = await FetchEquipmentAsync(progress?.DeriveSubtask(60, 100), cancellationToken);

        DofusDbItem[] ingredients = equipments
            .Select(e => recipesDict.GetValueOrDefault(e.Id!.Value))
            .OfType<DofusDbRecipe>()
            .SelectMany(r => r.Ingredients ?? [])
            .DistinctBy(i => i.Id!.Value)
            .ToArray();
        return (characteristicsDict, recipesDict, equipments, ingredients);
    }

    void CreateIngredients(
        DofusDbItem[] ingredients,
        IReadOnlySet<long> equipmentIds,
        ProgressSync<ProgressMessage>? progress)
    {
        ProgressSync<ProgressMessage>? ingredientsProgress = progress?.DeriveSubtask(50, 70);
        for (int index = 0; index < ingredients.Length; index++)
        {
            ingredientsProgress?.ReportStep($"Création des ingrédients {index}/{ingredients.Length}", index, ingredients.Length);
            DofusDbItem ingredient = ingredients[index];

            if (ingredient.Id is long ingredientId &&
                equipmentIds.Contains(ingredientId))
            {
                // Un équipement utilisé dans une recette reste un
                // équipement et ne doit pas apparaître comme ressource.
                continue;
            }

            Resource? resource = CreateResource(ingredient);
            if (resource is null)
            {
                logger.LogWarning("Could not map ingredient {Name} ({Id}).", ingredient.Name?.Fr ?? "???", ingredient.Id?.ToString() ?? "???");
                continue;
            }

            dbContext.Resources.Add(resource);
        }

        ingredientsProgress?.ReportStep($"Création des ingrédients {ingredients.Length}/{ingredients.Length}", ingredients.Length, ingredients.Length);
    }

    Task CreateEquipmentsAsync(
        Dictionary<long, DofusDbCharacteristic> characteristicsDict,
        DofusDbItem[] equipments,
        ProgressSync<ProgressMessage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ProgressSync<ProgressMessage>? equipmentsProgress = progress?.DeriveSubtask(70, 100);
        for (int index = 0; index < equipments.Length; index++)
        {
            equipmentsProgress?.ReportStep($"Création des équipements {index}/{equipments.Length}", index, equipments.Length);

            DofusDbItem dofusDbItem = equipments[index];
            if (!dofusDbItem.Id.HasValue)
            {
                logger.LogWarning("Could not map equipment {Name} ({Id}).", dofusDbItem.Name?.Fr ?? "???", dofusDbItem.Id?.ToString() ?? "???");
                continue;
            }

            Equipment? equipment =
                CreateEquipment(
                    dofusDbItem,
                    characteristicsDict
                );
            if (equipment is null)
            {
                logger.LogWarning("Could not map equipment {Name} ({Id}).", dofusDbItem.Name?.Fr ?? "???", dofusDbItem.Id?.ToString() ?? "???");
                continue;
            }
            dbContext.Equipments.Add(equipment);
        }

        equipmentsProgress?.ReportStep($"Création des équipements {equipments.Length}/{equipments.Length}", equipments.Length, equipments.Length);

        return Task.CompletedTask;
    }

    Equipment? CreateEquipment(
        DofusDbItem dofusDbItem,
        Dictionary<long, DofusDbCharacteristic> characteristics
    )
    {
        if (dofusDbItem.Id is null)
        {
            return null;
        }

        Equipment equipment = new(dofusDbItem.Id.Value)
        {
            DofusDbIconId = dofusDbItem.IconId,
            Level = dofusDbItem.Level ?? 0,
            Name = dofusDbItem.Name?.Fr ?? "???",
            Type = EquipmentTypeExtensions.EquipmentTypeFromDofusDbTypeId(dofusDbItem.TypeId ?? 0) ?? EquipmentType.MagicWeapon
        };

        CreateCharacteristicLines(
            dofusDbItem,
            equipment,
            characteristics
        );

        return equipment;
    }

    async Task CreateRecipesAsync(
        Dictionary<long, DofusDbRecipe> recipes,
        DofusDbItem[] dofusDbEquipments,
        ProgressSync<ProgressMessage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Dictionary<long, Equipment> equipments =
            await dbContext.Equipments
                .ToDictionaryAsync(
                    equipment =>
                        equipment.DofusDbId,
                    cancellationToken
                );

        Dictionary<long, Resource> resources =
            await dbContext.Resources
                .ToDictionaryAsync(
                    resource =>
                        resource.DofusDbId,
                    cancellationToken
                );

        for (int index = 0;
            index < dofusDbEquipments.Length;
            index++)
        {
            progress?.ReportStep(
                $"Création des recettes {index}/{dofusDbEquipments.Length}",
                index,
                dofusDbEquipments.Length
            );

            DofusDbItem dofusDbItem =
                dofusDbEquipments[index];

            if (dofusDbItem.Id is not long equipmentId ||
                dofusDbItem.HasRecipe != true ||
                !recipes.TryGetValue(
                    equipmentId,
                    out DofusDbRecipe? recipe) ||
                recipe.Ingredients is null ||
                recipe.Quantities is null ||
                !equipments.TryGetValue(
                    equipmentId,
                    out Equipment? equipment))
            {
                continue;
            }

            int ingredientCount =
                Math.Min(
                    recipe.Ingredients.Count,
                    recipe.Quantities.Count
                );

            for (int ingredientIndex = 0;
                ingredientIndex < ingredientCount;
                ingredientIndex++)
            {
                DofusDbItem ingredient =
                    recipe.Ingredients[
                        ingredientIndex
                    ];

                if (ingredient.Id is not long ingredientId)
                {
                    continue;
                }

                int quantity =
                    recipe.Quantities[
                        ingredientIndex
                    ];

                if (equipments.TryGetValue(
                    ingredientId,
                    out Equipment? ingredientEquipment))
                {
                    equipment.EquipmentRecipe.Add(
                        new EquipmentRecipeEntry(
                            equipment,
                            ingredientEquipment,
                            quantity
                        )
                    );

                    continue;
                }

                if (resources.TryGetValue(
                    ingredientId,
                    out Resource? resource))
                {
                    equipment.Recipe.Add(
                        new RecipeEntry(
                            equipment,
                            resource,
                            quantity
                        )
                    );

                    continue;
                }

                logger.LogWarning(
                    "Could not map recipe ingredient {Name} ({Id}) for equipment {EquipmentName} ({EquipmentId}).",
                    ingredient.Name?.Fr ?? "???",
                    ingredientId,
                    equipment.Name,
                    equipment.DofusDbId
                );
            }
        }

        progress?.ReportStep(
            $"Création des recettes {dofusDbEquipments.Length}/{dofusDbEquipments.Length}",
            dofusDbEquipments.Length,
            dofusDbEquipments.Length
        );
    }

    static Resource? CreateResource(DofusDbItem dofusDbItem)
    {
        if (dofusDbItem.Id is null)
        {
            return null;
        }

        Resource resource = new(dofusDbItem.Id.Value)
        {
            DofusDbIconId = dofusDbItem.IconId,
            Level = dofusDbItem.Level ?? 0,
            Name = dofusDbItem.Name?.Fr ?? "???"
        };

        return resource;
    }

    async Task RefreshRunesAsync(
        Dictionary<long, DofusDbCharacteristic>
            characteristicsDict,
        ProgressSync<ProgressMessage>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        DofusDbItem[] dofusDbRunes =
            await dofusDbQueryProvider
                .Items()
                .Where(item =>
                    item.TypeId == 78)
                .ExecuteAsync(
                    progress?
                        .DeriveSubtask(
                            0,
                            50
                        )
                        .ToMultiSearchProgress(
                            "Récupération des runes"
                        ),
                    cancellationToken
                )
                .Where(item =>
                    item.Id.HasValue)
                .ToArrayAsync(
                    cancellationToken
                );

        ProgressSync<ProgressMessage>?
            updateProgress =
                progress?.DeriveSubtask(
                    50,
                    100
                );

        List<Rune> runes = [];
        bool hasUnrecognizedBaseEffects = false;

        int skippedCount =
            0;

        for (
            int index = 0;
            index < dofusDbRunes.Length;
            index++)
        {
            DofusDbItem dofusDbRune =
                dofusDbRunes[index];

            updateProgress?.ReportStep(
                $"Mise à jour des runes " +
                $"{index}/{dofusDbRunes.Length}",
                index,
                dofusDbRunes.Length
            );

            Characteristic? characteristic =
                ResolveRuneCharacteristic(
                    dofusDbRune,
                    characteristicsDict
                );

            if (characteristic is null)
            {
                hasUnrecognizedBaseEffects |= dofusDbRune.PossibleEffects?.Any(effect => effect.BaseEffectId is > 0) == true;
                skippedCount++;

                logger.LogInformation(
                    "Skipping non-characteristic or unsupported rune {Name} ({Id}).",
                    dofusDbRune.Name?.Fr ??
                        "???",
                    dofusDbRune.Id?
                        .ToString() ??
                        "???"
                );

                continue;
            }

            Rune? rune =
                CreateRune(
                    dofusDbRune
                );

            if (rune is null)
            {
                skippedCount++;

                continue;
            }

            rune.Characteristic =
                characteristic.Value;

            runes.Add(rune);
        }

        if (hasUnrecognizedBaseEffects && runes.All(rune => rune.Characteristic == Characteristic.Hunting))
            throw new InvalidDataException("Le format des effets du catalogue de runes n’a pas pu être reconnu.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Runes.ExecuteDeleteAsync(cancellationToken);
        dbContext.Runes.AddRange(runes);
        await EnsureRuneCatalogUpgradeMarkerAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        updateProgress?.ReportStep(
            $"Mise à jour des runes " +
            $"{dofusDbRunes.Length}/{dofusDbRunes.Length}",
            dofusDbRunes.Length,
            dofusDbRunes.Length
        );

        logger.LogInformation(
            "Rune catalog rebuilt from DofusDB: {CreatedCount} created, {SkippedCount} skipped, {TotalCount} type-78 items received.",
            runes.Count,
            skippedCount,
            dofusDbRunes.Length
        );
    }

    static Characteristic?
        ResolveRuneCharacteristic(
            DofusDbItem dofusDbRune,
            Dictionary<
                long,
                DofusDbCharacteristic>
                characteristicsDict)
    {
        // DofusDB encode actuellement la Rune de chasse
        // avec characteristic = 0. Elle correspond néanmoins
        // bien à la caractéristique Hunting de BestCrush.
        if (dofusDbRune.Id ==
                10057 ||
            string.Equals(
                dofusDbRune.Name?.Fr,
                "Rune de chasse",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return Characteristic.Hunting;
        }

        foreach (var effect in dofusDbRune.Effects ?? [])
        {
            if (effect.Characteristic is { } id &&
                characteristicsDict.GetValueOrDefault(id)?.Keyword is { } keyword &&
                CharacteristicExtensions.CharacteristicFromDofusDbKeyword(keyword) is { } characteristic)
                return characteristic;
        }

        foreach (var effect in dofusDbRune.PossibleEffects ?? [])
        {
            if (RuneBaseEffects.Resolve(effect.BaseEffectId) is { } characteristic)
                return characteristic;
        }

        return null;
    }


    static Rune? CreateRune(DofusDbItem dofusDbItem)
    {
        if (dofusDbItem.Id is null)
        {
            return null;
        }

        Rune rune = new(dofusDbItem.Id.Value)
        {
            DofusDbIconId = dofusDbItem.IconId,
            Level = dofusDbItem.Level ?? 0,
            Name = dofusDbItem.Name?.Fr ?? "???"
        };

        return rune;
    }
}
