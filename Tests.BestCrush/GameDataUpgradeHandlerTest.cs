using BestCrush.Domain;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services.Upgrades;
using DofusSharp.DofusDb.ApiClients;
using DofusSharp.DofusDb.ApiClients.Models.Characteristics;
using DofusSharp.DofusDb.ApiClients.Models.Common;
using DofusSharp.DofusDb.ApiClients.Models.Items;
using DofusSharp.DofusDb.ApiClients.Models.Jobs;
using DofusSharp.DofusDb.ApiClients.Search;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Tests.BestCrush.Utils;

namespace Tests.BestCrush;

public class GameDataUpgradeHandlerTest : IDisposable
{
    static readonly string[] EquipmentTypeIds =
        ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "16", "17", "18", "121", "311", "19", "20", "21", "22", "82", "114", "151", "271"];

    const string RuneTypeId = "78";
    const string RuneCatalogVersion = "dofusdb-only-v2";

    readonly TestDatabase _testDatabase;
    readonly BestCrushDbContext _context;
    readonly Mock<IDofusDbTableClient<DofusDbCharacteristic>> _dofusDbCharacteristicsClientMock;
    readonly Mock<IDofusDbTableClient<DofusDbItem>> _dofusDbItemsClientMock;
    readonly Mock<IDofusDbTableClient<DofusDbRecipe>> _dofusDbRecipesClientMock;
    readonly GameDataUpgradeHandler _handler;

    public GameDataUpgradeHandlerTest()
    {
        _testDatabase = new TestDatabase();
        _context = _testDatabase.CreateContext();

        Mock<IDofusDbClientsFactory> clientsFactory = new();
        _dofusDbCharacteristicsClientMock = CommonMocks.TableClient<DofusDbCharacteristic>();
        clientsFactory.Setup(f => f.Characteristics()).Returns(_dofusDbCharacteristicsClientMock.Object);
        _dofusDbItemsClientMock = CommonMocks.TableClient<DofusDbItem>();
        clientsFactory.Setup(f => f.Items()).Returns(_dofusDbItemsClientMock.Object);
        _dofusDbRecipesClientMock = CommonMocks.TableClient<DofusDbRecipe>();
        clientsFactory.Setup(f => f.Recipes()).Returns(_dofusDbRecipesClientMock.Object);

        IDofusDbQueryProvider queryProvider = DofusDbQuery.Create(clientsFactory.Object);
        _handler = new GameDataUpgradeHandler(_context, queryProvider, Mock.Of<ILogger<GameDataUpgradeHandler>>());
    }

    public void Dispose() => _testDatabase.Dispose();

    [Fact]
    public async Task ShouldNotUpgrade_WhenVersionIsTheSame()
    {
        _context.Upgrades.Add(new Upgrade { Kind = UpgradeKind.DofusDb, NewVersion = "1.2.3", UpgradeDate = DateTime.Now });
        _context.Upgrades.Add(new Upgrade { Kind = UpgradeKind.RuneCatalog, NewVersion = RuneCatalogVersion, UpgradeDate = DateTime.Now });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        _dofusDbCharacteristicsClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        _dofusDbItemsClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        _dofusDbRecipesClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShouldUpgrade_WhenNoUpgradeExists()
    {
        await _handler.UpgradeAsync(new Version(1, 2, 3));

        _dofusDbCharacteristicsClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()));
        _dofusDbItemsClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()));
        _dofusDbRecipesClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("1.2.1")]
    [InlineData("1.2.4")]
    public async Task ShouldUpgrade_WhenVersionIsDifferent(string currentVersion)
    {
        _context.Upgrades.Add(new Upgrade { Kind = UpgradeKind.DofusDb, NewVersion = currentVersion, UpgradeDate = DateTime.Now });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        _dofusDbCharacteristicsClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()));
        _dofusDbItemsClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()));
        _dofusDbRecipesClientMock.Verify(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ShouldSearchForEquipmentsByTypeIds()
    {
        await _handler.UpgradeAsync(new Version(1, 2, 3));
        _dofusDbItemsClientMock.Verify(q => q.SearchAsync(
                                           It.Is<DofusDbSearchQuery>(sq => sq
                                                                         .Predicates.OfType<DofusDbSearchPredicate.In>()
                                                                         .Any(p => p.Field == "typeId" && p.Value.SequenceEqual(EquipmentTypeIds))
                                           ),
                                           It.IsAny<CancellationToken>()
                                       )
        );
    }

    [Fact]
    public async Task ShouldSearchForRunesByTypeId()
    {
        await _handler.UpgradeAsync(new Version(1, 2, 3));
        _dofusDbItemsClientMock.Verify(q => q.SearchAsync(
                                           It.Is<DofusDbSearchQuery>(sq => sq.Predicates.OfType<DofusDbSearchPredicate.Eq>().Any(p => p.Field == "typeId" && p.Value == RuneTypeId)),
                                           It.IsAny<CancellationToken>()
                                       )
        );
    }

    [Fact]
    public async Task ShouldRegisterEquipment_WhenEmpty()
    {
        _dofusDbCharacteristicsClientMock
            .Setup(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbCharacteristic>
                {
                    Data =
                    [
                        new DofusDbCharacteristic { Id = 147, Keyword = "actionPoints" },
                        new DofusDbCharacteristic { Id = 258, Keyword = "fireElementResistPercent" }
                    ],
                    Total = 2, Limit = 2, Skip = 0
                }
            );

        _dofusDbRecipesClientMock
            .Setup(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbRecipe>
                {
                    Data =
                    [
                        new DofusDbRecipe
                        {
                            Id = 147,
                            ResultId = 1,
                            Ingredients =
                            [
                                new DofusDbItem
                                {
                                    Id = 987,
                                    IconId = 258,
                                    Level = 357,
                                    Name = new DofusDbMultiLangString { Fr = "INGREDIENT_NAME" }
                                }
                            ],
                            Quantities = [5]
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        _dofusDbItemsClientMock
            .Setup(q => q.SearchAsync(It.Is<DofusDbSearchQuery>(query => IsEquipmentQuery(query)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbItem>
                {
                    Data =
                    [
                        new DofusDbItem
                        {
                            Id = 1,
                            TypeId = EquipmentType.Boots.ToDofusDbItemTypeId(),
                            IconId = 147,
                            Level = 159,
                            Name = new DofusDbMultiLangString { Fr = "ITEM_NAME" },
                            Effects = [new DofusDbItemEffect { Characteristic = 147, From = 4, To = 6 }, new DofusDbItemEffect { Characteristic = 258, From = 2, To = 8 }],
                            HasRecipe = true,
                            RecipeIds = [147]
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Equipment[] equipments = await _context.Equipments.ToArrayAsync();
        Equipment? equipment = equipments.Should().ContainSingle().Which;

        Resource[] resources = await _context.Resources.ToArrayAsync();
        Resource? resource = resources.Should().ContainSingle().Which;

        await Verify(((IItem[])[equipment, resource]).OrderBy(i => i.DofusDbId));
    }

    [Fact]
    public async Task ShouldUpdateEquipment_WhenExistsAlready()
    {
        _dofusDbItemsClientMock
            .Setup(q => q.SearchAsync(It.Is<DofusDbSearchQuery>(query => IsEquipmentQuery(query)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbItem>
                {
                    Data =
                    [
                        new DofusDbItem
                        {
                            Id = 1,
                            TypeId = EquipmentType.Boots.ToDofusDbItemTypeId(),
                            IconId = 147,
                            Level = 159,
                            Name = new DofusDbMultiLangString { Fr = "ITEM_NAME" }
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        Equipment dbEquipment = new(1)
        {
            DofusDbIconId = 11111,
            Level = 22222,
            Name = "OLD_NAME",
            Type = EquipmentType.Belt
        };

        _context.Equipments.Add(dbEquipment);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Equipment[] equipments = await _context.Equipments.ToArrayAsync();
        Equipment? equipment = equipments.Should().ContainSingle().Which;

        await Verify(equipment);
    }

    [Fact]
    public async Task ShouldUpdateEquipmentCharacteristics_WhenExistsAlready()
    {
        _dofusDbCharacteristicsClientMock
            .Setup(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbCharacteristic>
                {
                    Data =
                    [
                        new DofusDbCharacteristic { Id = 147, Keyword = "actionPoints" },
                        new DofusDbCharacteristic { Id = 258, Keyword = "fireElementResistPercent" }
                    ],
                    Total = 2, Limit = 2, Skip = 0
                }
            );

        _dofusDbItemsClientMock
            .Setup(q => q.SearchAsync(It.Is<DofusDbSearchQuery>(query => IsEquipmentQuery(query)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbItem>
                {
                    Data =
                    [
                        new DofusDbItem
                        {
                            Id = 1,
                            TypeId = EquipmentType.Boots.ToDofusDbItemTypeId(),
                            IconId = 147,
                            Level = 159,
                            Name = new DofusDbMultiLangString { Fr = "ITEM_NAME" },
                            Effects = [new DofusDbItemEffect { Characteristic = 147, From = 4, To = 6 }, new DofusDbItemEffect { Characteristic = 258, From = 2, To = 8 }]
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        Equipment dbEquipment = new(1);
        dbEquipment.Characteristics.Add(new ItemCharacteristicLine(dbEquipment, Characteristic.DamageFlatAir, 3, 6));
        dbEquipment.Characteristics.Add(new ItemCharacteristicLine(dbEquipment, Characteristic.Ap, 1, 2));

        _context.Equipments.Add(dbEquipment);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Equipment[] equipments = await _context.Equipments.ToArrayAsync();
        Equipment? equipment = equipments.Should().ContainSingle().Which;

        await Verify(equipment);
    }

    [Fact]
    public async Task ShouldUpdateEquipmentRecipe_WhenExistsAlready()
    {
        _dofusDbRecipesClientMock
            .Setup(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbRecipe>
                {
                    Data =
                    [
                        new DofusDbRecipe
                        {
                            Id = 147,
                            ResultId = 1,
                            Ingredients =
                            [
                                new DofusDbItem
                                {
                                    Id = 987,
                                    IconId = 258,
                                    Level = 357,
                                    Name = new DofusDbMultiLangString { Fr = "INGREDIENT_NAME1" }
                                },
                                new DofusDbItem
                                {
                                    Id = 321,
                                    IconId = 369,
                                    Level = 495,
                                    Name = new DofusDbMultiLangString { Fr = "INGREDIENT_NAME2" }
                                }
                            ],
                            Quantities = [5, 8]
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        _dofusDbItemsClientMock
            .Setup(q => q.SearchAsync(It.Is<DofusDbSearchQuery>(query => IsEquipmentQuery(query)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbItem>
                {
                    Data =
                    [
                        new DofusDbItem
                        {
                            Id = 1,
                            TypeId = EquipmentType.Boots.ToDofusDbItemTypeId(),
                            IconId = 147,
                            Level = 159,
                            Name = new DofusDbMultiLangString { Fr = "ITEM_NAME" },
                            Effects = [new DofusDbItemEffect { Characteristic = 147, From = 4, To = 6 }, new DofusDbItemEffect { Characteristic = 258, From = 2, To = 8 }],
                            HasRecipe = true,
                            RecipeIds = [147]
                        }

                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        Resource dbResource1 = new(654) { DofusDbIconId = 111111, Level = 222222, Name = "OLD_INGREDIENT_NAME1" };
        Resource dbResource2 = new(987) { DofusDbIconId = 333333, Level = 444444, Name = "OLD_INGREDIENT_NAME" };
        Equipment dbEquipment = new(1);
        dbEquipment.Recipe.Add(new RecipeEntry(dbEquipment, dbResource1, 2));
        dbEquipment.Recipe.Add(new RecipeEntry(dbEquipment, dbResource2, 4));

        _context.Resources.Add(dbResource1);
        _context.Resources.Add(dbResource2);
        _context.Equipments.Add(dbEquipment);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Equipment[] equipments = await _context.Equipments.ToArrayAsync();
        Equipment? equipment = equipments.Should().ContainSingle().Which;

        Resource[] resources = await _context.Resources.ToArrayAsync();
        resources.Should().HaveCount(2);

        await Verify(((IItem[])[equipment, ..resources]).OrderBy(i => i.DofusDbId).ToArray());
    }

    [Fact]
    public async Task ShouldRemoveEquipmentAndResources_WhenEquipmentIsRemoved()
    {
        Resource dbResource = new(987) { DofusDbIconId = 333333, Level = 444444, Name = "OLD_INGREDIENT_NAME" };
        Equipment dbEquipment = new(1)
        {
            DofusDbIconId = 11111,
            Level = 22222,
            Name = "OLD_NAME",
            Type = EquipmentType.Belt
        };
        dbEquipment.Characteristics.Add(new ItemCharacteristicLine(dbEquipment, Characteristic.Ap, 1, 2));
        dbEquipment.Recipe.Add(new RecipeEntry(dbEquipment, dbResource, 4));

        _context.Resources.Add(dbResource);
        _context.Equipments.Add(dbEquipment);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Equipment[] equipments = await _context.Equipments.ToArrayAsync();
        equipments.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRemoveResource_WhenEquipmentRecipeIsRemoved()
    {
        _dofusDbItemsClientMock
            .Setup(q => q.SearchAsync(It.Is<DofusDbSearchQuery>(query => IsEquipmentQuery(query)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbItem>
                {
                    Data =
                    [
                        new DofusDbItem
                        {
                            Id = 1,
                            TypeId = EquipmentType.Boots.ToDofusDbItemTypeId(),
                            IconId = 147,
                            Level = 159,
                            Name = new DofusDbMultiLangString { Fr = "ITEM_NAME" }
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        Resource dbResource = new(987);
        Equipment dbEquipment = new(1);
        dbEquipment.Recipe.Add(new RecipeEntry(dbEquipment, dbResource, 4));

        _context.Resources.Add(dbResource);
        _context.Equipments.Add(dbEquipment);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Resource[] resources = await _context.Resources.ToArrayAsync();
        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotRemoveResource_WhenOtherEquipmentRecipeUsesIt()
    {
        _dofusDbRecipesClientMock
            .Setup(q => q.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbRecipe>
                {
                    Data =
                    [
                        new DofusDbRecipe
                        {
                            Id = 147,
                            ResultId = 1,
                            Ingredients =
                            [
                                new DofusDbItem
                                {
                                    Id = 987,
                                    IconId = 258,
                                    Level = 357,
                                    Name = new DofusDbMultiLangString { Fr = "INGREDIENT_NAME" }
                                }
                            ],
                            Quantities = [5]
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );

        _dofusDbItemsClientMock
            .Setup(q => q.SearchAsync(It.Is<DofusDbSearchQuery>(query => IsEquipmentQuery(query)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DofusDbSearchResult<DofusDbItem>
                {
                    Data =
                    [
                        new DofusDbItem
                        {
                            Id = 1,
                            TypeId = EquipmentType.Boots.ToDofusDbItemTypeId(),
                            IconId = 147,
                            Level = 159,
                            Name = new DofusDbMultiLangString { Fr = "ITEM_NAME" },
                            HasRecipe = true,
                            RecipeIds = [147]
                        }
                    ],
                    Total = 1, Limit = 1, Skip = 0
                }
            );
        Resource dbResource = new(987);
        Equipment dbEquipment1 = new(1);
        dbEquipment1.Recipe.Add(new RecipeEntry(dbEquipment1, dbResource, 4));
        Equipment dbEquipment2 = new(2);
        dbEquipment2.Recipe.Add(new RecipeEntry(dbEquipment2, dbResource, 5));

        _context.Resources.Add(dbResource);
        _context.Equipments.Add(dbEquipment1);
        _context.Equipments.Add(dbEquipment2);

        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Resource[] resources = await _context.Resources.ToArrayAsync();
        Resource? resource = resources.Should().ContainSingle().Which;

        await Verify(resource);
    }

    [Fact]
    public async Task ShouldRegisterRune_WhenEmpty()
    {
        SetDofusDbCharacteristics(new DofusDbCharacteristic { Id = 147, Keyword = "actionPoints" });
        SetDofusDbRunes(CreateApRune());

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Rune[] runes = await _context.Runes.ToArrayAsync();
        Rune? rune = runes.Should().ContainSingle().Which;
        (await _context.Equipments.AnyAsync()).Should().BeFalse();

        await Verify(rune);
    }

    [Fact]
    public async Task ShouldUpdateRune_WhenExistsAlready()
    {
        SetDofusDbCharacteristics(new DofusDbCharacteristic { Id = 147, Keyword = "actionPoints" });
        SetDofusDbRunes(CreateApRune());

        _context.Runes.Add(new Rune(123) { Characteristic = Characteristic.ApReduction, DofusDbIconId = 111111, Level = 2222222, Name = "OLD_NAME" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Rune[] runes = await _context.Runes.ToArrayAsync();
        Rune? rune = runes.Should().ContainSingle().Which;

        await Verify(rune);
    }

    [Fact]
    public async Task ShouldRemoveRune_WhenRuneIsRemove()
    {
        _context.Runes.Add(new Rune(123) { Characteristic = Characteristic.ApReduction, DofusDbIconId = 111111, Level = 2222222, Name = "OLD_NAME" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        Rune[] runes = await _context.Runes.ToArrayAsync();
        runes.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRebuildOnlyRunes_WhenVersionIsTheSameAndRuneCatalogMarkerIsMissing()
    {
        Resource resource = new(987) { Name = "EXISTING_RESOURCE" };
        Equipment equipment = new(1) { Name = "EXISTING_EQUIPMENT", Level = 100, Type = EquipmentType.Boots };
        RecipeEntry recipe = new(equipment, resource, 5);
        equipment.Recipe.Add(recipe);
        equipment.Characteristics.Add(new ItemCharacteristicLine(equipment, Characteristic.Ap, 1, 2));
        _context.Equipments.Add(equipment);
        _context.Runes.Add(new Rune(999) { Characteristic = Characteristic.Ap, Name = "OLD_RUNE" });
        _context.Upgrades.Add(new Upgrade { Kind = UpgradeKind.DofusDb, NewVersion = "1.2.3", UpgradeDate = DateTime.Now });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        SetDofusDbCharacteristics(new DofusDbCharacteristic { Id = 147, Keyword = "actionPoints" });
        SetDofusDbRunes(CreateApRune());

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        using BestCrushDbContext persisted = _testDatabase.CreateContext();
        Rune rune = (await persisted.Runes.ToArrayAsync()).Should().ContainSingle().Which;
        rune.DofusDbId.Should().Be(123);
        rune.Characteristic.Should().Be(Characteristic.Ap);
        Equipment preserved = await persisted.Equipments
            .Include(item => item.Characteristics)
            .Include(item => item.Recipe).ThenInclude(entry => entry.Resource)
            .SingleAsync();
        preserved.Id.Should().Be(equipment.Id);
        preserved.Name.Should().Be("EXISTING_EQUIPMENT");
        preserved.Level.Should().Be(100);
        preserved.Type.Should().Be(EquipmentType.Boots);
        ItemCharacteristicLine characteristic = preserved.Characteristics.Should().ContainSingle().Which;
        characteristic.Characteristic.Should().Be(Characteristic.Ap);
        characteristic.From.Should().Be(1);
        characteristic.To.Should().Be(2);
        RecipeEntry preservedRecipe = preserved.Recipe.Should().ContainSingle().Which;
        preservedRecipe.Id.Should().Be(recipe.Id);
        preservedRecipe.Count.Should().Be(5);
        Resource preservedResource = await persisted.Resources.SingleAsync();
        preservedResource.Id.Should().Be(resource.Id);
        preservedResource.Name.Should().Be("EXISTING_RESOURCE");
        preservedRecipe.Resource.Id.Should().Be(resource.Id);
        (await persisted.Upgrades.CountAsync(upgrade => upgrade.Kind == UpgradeKind.DofusDb)).Should().Be(1);
        Upgrade marker = await persisted.Upgrades.SingleAsync(upgrade => upgrade.Kind == UpgradeKind.RuneCatalog);
        marker.NewVersion.Should().Be(RuneCatalogVersion);

        _dofusDbItemsClientMock.Verify(client => client.SearchAsync(
            It.Is<DofusDbSearchQuery>(query => IsEquipmentQuery(query)), It.IsAny<CancellationToken>()), Times.Never);
        _dofusDbRecipesClientMock.Verify(client => client.SearchAsync(
            It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShouldKeepSingleRuneCatalogMarker_WhenGameVersionChangesAgain()
    {
        Upgrade marker = new() { Kind = UpgradeKind.RuneCatalog, NewVersion = RuneCatalogVersion, UpgradeDate = DateTime.Now };
        _context.Upgrades.Add(marker);
        _context.Upgrades.Add(new Upgrade { Kind = UpgradeKind.DofusDb, NewVersion = "1.2.2", UpgradeDate = DateTime.Now });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _handler.UpgradeAsync(new Version(1, 2, 3));
        await _handler.UpgradeAsync(new Version(1, 2, 4));

        using BestCrushDbContext persisted = _testDatabase.CreateContext();
        Upgrade preserved = await persisted.Upgrades.SingleAsync(upgrade => upgrade.Kind == UpgradeKind.RuneCatalog);
        preserved.Id.Should().Be(marker.Id);
        preserved.NewVersion.Should().Be(RuneCatalogVersion);
        (await persisted.Upgrades.CountAsync(upgrade => upgrade.Kind == UpgradeKind.DofusDb)).Should().Be(3);
    }

    [Fact]
    public async Task ShouldRegisterBasicPaAndRaRunes_FromDofusDb()
    {
        SetDofusDbCharacteristics(new DofusDbCharacteristic { Id = 147, Keyword = "vitality" });
        SetDofusDbRunes(
            new DofusDbItem { Id = 101, TypeId = 78, Name = new DofusDbMultiLangString { Fr = "Rune Vi" }, Effects = [new DofusDbItemEffect { Characteristic = 147, From = 1 }] },
            new DofusDbItem { Id = 102, TypeId = 78, Name = new DofusDbMultiLangString { Fr = "Rune Pa Vi" }, Effects = [new DofusDbItemEffect { Characteristic = 147, From = 3 }] },
            new DofusDbItem { Id = 103, TypeId = 78, Name = new DofusDbMultiLangString { Fr = "Rune Ra Vi" }, Effects = [new DofusDbItemEffect { Characteristic = 147, From = 10 }] });

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        using BestCrushDbContext persisted = _testDatabase.CreateContext();
        Rune[] runes = await persisted.Runes.OrderBy(rune => rune.DofusDbId).ToArrayAsync();
        runes.Select(rune => rune.DofusDbId).Should().Equal(101L, 102L, 103L);
        runes.Select(rune => rune.Name).Should().Equal("Rune Vi", "Rune Pa Vi", "Rune Ra Vi");
        runes.Should().OnlyContain(rune => rune.Characteristic == Characteristic.Vitality);
        (await persisted.Equipments.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(10057L, "HUNTING_RUNE_BY_ID")]
    [InlineData(321L, "Rune de chasse")]
    [InlineData(321L, "RUNE DE CHASSE")]
    public async Task ShouldIdentifyHuntingRune_WithoutMappedCharacteristic(long id, string name)
    {
        SetDofusDbRunes(new DofusDbItem
        {
            Id = id,
            TypeId = 78,
            Name = new DofusDbMultiLangString { Fr = name },
            Effects = [new DofusDbItemEffect { Characteristic = 0 }]
        });

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        using BestCrushDbContext persisted = _testDatabase.CreateContext();
        Rune rune = (await persisted.Runes.ToArrayAsync()).Should().ContainSingle().Which;
        rune.DofusDbId.Should().Be(id);
        rune.Name.Should().Be(name);
        rune.Characteristic.Should().Be(Characteristic.Hunting);
    }

    [Fact]
    public async Task ShouldSkipRunes_WithoutSupportedCharacteristic()
    {
        SetDofusDbCharacteristics(new DofusDbCharacteristic { Id = 147, Keyword = "unsupported-statistic" });
        SetDofusDbRunes(
            new DofusDbItem { Id = 201, TypeId = 78, Name = new DofusDbMultiLangString { Fr = "Rune de Signature" } },
            new DofusDbItem { Id = 202, TypeId = 78, Effects = [new DofusDbItemEffect { Characteristic = 999 }] },
            new DofusDbItem { Id = 203, TypeId = 78, Effects = [new DofusDbItemEffect { Characteristic = 147 }] });

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        using BestCrushDbContext persisted = _testDatabase.CreateContext();
        (await persisted.Runes.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldPreserveObservationsAndCrushHistory_WhenRebuildingGameData()
    {
        DateTime observedAt = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
        MarketPriceObservation price = new()
        {
            ObjectType = MarketObjectType.Equipment, DofusDbId = 1, ServerName = "TEST_SERVER",
            Price = 1200, Quantity = 1, Source = MarketPriceSource.Manual, ObservedAtUtc = observedAt
        };
        CoefficientObservation coefficient = new()
        {
            DofusDbId = 1, ServerName = "TEST_SERVER", CoefficientPercent = 371,
            Source = CoefficientSource.InGameAutomatic, ObservedAtUtc = observedAt
        };
        CrushHistorySession session = new("TEST_SERVER", observedAt, observedAt.AddSeconds(1), 1000, 10, 900);
        CrushHistoryEquipment historyEquipment = new(session, 1, 147, "EQUIPMENT_AT_CAPTURE", 371, CoefficientSource.InGameAutomatic, 2);
        CrushHistoryRune historyRune = new(session, 123, 269, "RUNE_AT_CAPTURE", 10, 1000);
        CrushHistoryRuneLot historyLot = new(historyRune, 1, 10, 1000, false, MarketPriceSource.Manual, observedAt);
        session.Equipments.Add(historyEquipment);
        session.Runes.Add(historyRune);
        historyRune.Lots.Add(historyLot);
        _context.Equipments.Add(new Equipment(1) { Name = "OLD_EQUIPMENT" });
        _context.Runes.Add(new Rune(123) { Name = "OLD_RUNE", Characteristic = Characteristic.Ap });
        _context.MarketPriceObservations.Add(price);
        _context.CoefficientObservations.Add(coefficient);
        _context.CrushHistorySessions.Add(session);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        SetDofusDbCharacteristics(new DofusDbCharacteristic { Id = 147, Keyword = "actionPoints" });
        SetDofusDbRunes(CreateApRune());

        await _handler.UpgradeAsync(new Version(1, 2, 3));

        using BestCrushDbContext persisted = _testDatabase.CreateContext();
        (await persisted.Equipments.AnyAsync()).Should().BeFalse();
        (await persisted.Runes.SingleAsync()).Name.Should().Be("RUNE_NAME");
        (await persisted.MarketPriceObservations.SingleAsync()).Should().BeEquivalentTo(price);
        (await persisted.CoefficientObservations.SingleAsync()).Should().BeEquivalentTo(coefficient);
        CrushHistorySession preserved = await persisted.CrushHistorySessions
            .Include(item => item.Equipments)
            .Include(item => item.Runes).ThenInclude(rune => rune.Lots)
            .SingleAsync();
        preserved.Should().BeEquivalentTo(session, options => options.Excluding(item => item.Equipments).Excluding(item => item.Runes));
        preserved.Equipments.Should().ContainSingle().Which.Should().BeEquivalentTo(historyEquipment, options => options.Excluding(item => item.Session));
        CrushHistoryRune preservedRune = preserved.Runes.Should().ContainSingle().Which;
        preservedRune.Should().BeEquivalentTo(historyRune, options => options.Excluding(item => item.Session).Excluding(item => item.Lots));
        preservedRune.Lots.Should().ContainSingle().Which.Should().BeEquivalentTo(historyLot, options => options.Excluding(item => item.Rune));
    }

    static DofusDbItem CreateApRune() => new()
    {
        Id = 123,
        TypeId = 78,
        IconId = 269,
        Level = 159,
        Name = new DofusDbMultiLangString { Fr = "RUNE_NAME" },
        Effects = [new DofusDbItemEffect { Characteristic = 147, From = 1 }]
    };

    void SetDofusDbCharacteristics(params DofusDbCharacteristic[] characteristics)
    {
        _dofusDbCharacteristicsClientMock
            .Setup(client => client.SearchAsync(It.IsAny<DofusDbSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DofusDbSearchResult<DofusDbCharacteristic>
            {
                Data = characteristics, Total = characteristics.Length, Limit = characteristics.Length, Skip = 0
            });
    }

    void SetDofusDbRunes(params DofusDbItem[] runes)
    {
        _dofusDbItemsClientMock
            .Setup(client => client.SearchAsync(It.Is<DofusDbSearchQuery>(query => IsRuneQuery(query)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DofusDbSearchResult<DofusDbItem>
            {
                Data = runes, Total = runes.Length, Limit = runes.Length, Skip = 0
            });
    }

    static bool IsEquipmentQuery(DofusDbSearchQuery query) =>
        query.Predicates.OfType<DofusDbSearchPredicate.In>().Any(predicate => predicate.Field == "typeId");

    static bool IsRuneQuery(DofusDbSearchQuery query) =>
        query.Predicates.OfType<DofusDbSearchPredicate.Eq>().Any(predicate => predicate.Field == "typeId" && predicate.Value == RuneTypeId);
}
