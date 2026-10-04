using System.Text.Json;
using BestCrush.NetworkProbe.Protocol;
using FluentAssertions;

namespace Tests.BestCrush.Network;

public sealed class ProtocolMapTest : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Tests.BestCrush.Protocol-" + Guid.NewGuid().ToString("N"));

    public ProtocolMapTest() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BundledMapAndMissingFileFallbackKeepCurrentSemanticKeys(bool useMissingFile)
    {
        string path = useMissingFile
            ? Path.Combine(_directory, "missing.json")
            : Path.Combine(AppContext.BaseDirectory, "NetworkFixtures", "protocol-map.json");
        if (!useMissingFile)
            File.Exists(path).Should().BeTrue("the actual production map must be copied, not silently replaced by defaults");

        ProtocolMap map = ProtocolMap.Load(path);

        map.ClientBuild.Should().Be("3.6.11.15");
        new[] { map.PriceList, map.CrushResult, map.ItemDetail, map.WorkshopSlotPut,
                map.PurchaseRequest, map.PurchaseOffer, map.PurchaseReceipt,
                map.InventoryAdd, map.InventoryQuantity, map.InventoryRemove,
                map.CraftPrepare, map.CraftOutput, map.SmithmagicRequest, map.SmithmagicBatchRequest,
                map.SmithmagicResult, map.SmithmagicAux, map.MarketListingRequest, map.MarketListingCreated }
            .Should().Equal("jzn", "kci", "kdb", "kec", "kei", "kef", "kbd", "isa", "isf", "irz",
                "kah", "iuq", "jze", "kcs", "kbu", "kar", "kcr", "kda");
        map.DiagnosticMessages.Should().BeEquivalentTo(useMissingFile
            ? new[] { "itt", "log", "kbd", "iut", "irl", "isf", "keb", "kcw", "kau", "kef", "irz" }
            : new[] { "itt", "log", "iut", "irl", "keb", "kcw", "kau", "kef" });
    }

    [Fact]
    public void ExistingPartialMapDoesNotFillMissingKeysFromFallbackAndKeepsCase()
    {
        var map = ReadJson("""
            { "price_list": "JZN", "diagnostic_messages": ["kef", "kef", "KEF", "", " ", null] }
            """);
        map.ClientBuild.Should().Be("unknown");
        map.PriceList.Should().Be("JZN");
        map.CrushResult.Should().BeNull();
        map.ItemDetail.Should().BeNull();
        map.PurchaseRequest.Should().BeNull();
        map.PurchaseOffer.Should().BeNull();
        map.DiagnosticMessages.Should().BeEquivalentTo(new[] { "kef", "KEF" });
    }

    [Theory]
    [InlineData("{\"crush_slot_put\":\"legacy\"}", "legacy")]
    [InlineData("{\"crush_slot_put\":\"legacy\",\"workshop_slot_put\":\"current\"}", "current")]
    [InlineData("{\"crush_slot_put\":\"legacy\",\"workshop_slot_put\":null}", null)]
    public void LegacyWorkshopKeyIsUsedOnlyWhenCurrentPropertyIsAbsent(string json, string? expected)
    {
        ReadJson(json).WorkshopSlotPut.Should().Be(expected);
    }

    [Fact]
    public void MalformedExistingMapThrowsInsteadOfUsingDefaults()
    {
        Action load = () => ReadJson("{");
        load.Should().Throw<JsonException>();
    }

    private ProtocolMap ReadJson(string json)
    {
        string path = Path.Combine(_directory, "map.json");
        File.WriteAllText(path, json);
        return ProtocolMap.Load(path);
    }
}
