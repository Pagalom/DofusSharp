using System.Text.Json;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ProtocolWireNormalizerTest
{
    [Fact]
    public void EmptyOrIdentityMappingKeepsOriginalBytes()
    {
        ProtocolMap map = LoadMap(
            """
            {
              "price_list": "jzn",
              "field_mappings": {
                "price_list": { "1": 1, "2": {"wire":2,"fields":{"2":2,"5":5,"6":6}} }
              }
            }
            """);

        byte[] wire = Market(42, MarketOffer(17, 42, 4, 20));
        map.NormalizeBody("jzn", wire).Should().BeSameAs(wire);
        map.NormalizeBody("unknown", wire).Should().BeSameAs(wire);
    }

    [Fact]
    public void ConfigOnlyCanRemapMarketIdsAndNestedOffers()
    {
        ProtocolMap map = LoadMap(
            """
            {
              "price_list": "qaa",
              "field_mappings": {
                "price_list": {
                  "1": 7,
                  "2": {"wire":9,"fields":{"2":8,"5":11,"6":12}}
                }
              }
            }
            """);

        byte[] wire = Message(
            Unsigned(7, 42),
            Bytes(9, Message(
                Unsigned(8, 42),
                Unsigned(11, 501),
                Bytes(12, Packed(5, 10, 20, 30)))));

        MarketObservation? market = SemanticDecoders.TryDecodeMarket(
            map.NormalizeBody("qaa", wire));

        market.Should().NotBeNull();
        market!.ItemId.Should().Be(42);
        market.Offers.Should().ContainSingle();
        market.Offers[0].OfferId.Should().Be(501);
        market.Offers[0].Ladder.Should().Equal(5UL, 10UL, 20UL, 30UL);
        map.NormalizeBody("jzn", wire).Should().BeSameAs(wire);
    }

    [Fact]
    public void ConfigOnlyCanRemapCrushRowsCoefficientsAndRunes()
    {
        ProtocolMap map = LoadMap(
            """
            {
              "crush_result": "qbb",
              "field_mappings": {
                "crush_result": {
                  "1": {"wire":7,"fields":{
                    "1":{"wire":8,"fields":{"1":7,"3":11}},
                    "2":9,
                    "3":10,
                    "4":12
                  }}
                }
              }
            }
            """);

        byte[] wire = Bytes(7, Message(
            Bytes(8, Message(
                Unsigned(7, 100),
                Unsigned(11, 2))),
            Float(9, 0.25f),
            Unsigned(10, 1234)));

        CrushObservation? crush = SemanticDecoders.TryDecodeCrush(
            map.NormalizeBody("qbb", wire));

        crush.Should().NotBeNull();
        crush!.Lines.Should().ContainSingle();
        crush.Lines[0].ItemUid.Should().Be(1234);
        crush.Lines[0].CoefficientPercent.Should().BeApproximately(25f, 0.00001f);
        crush.Lines[0].Runes.Should().ContainSingle()
            .Which.Should().Be(new RuneDrop(100, 2));
    }

    [Fact]
    public void ConfigOnlyCanRemapDeepItemAndStatFields()
    {
        ProtocolMap map = LoadMap(
            """
            {
              "item_detail": "qcc",
              "field_mappings": {
                "item_detail": {
                  "2": {"wire":4,"fields":{
                    "5":{"wire":9,"fields":{
                      "1":7,"2":8,"5":11,
                      "3":{"wire":10,"fields":{"1":6,"10":12}}
                    }}
                  }}
                }
              }
            }
            """);

        byte[] wire = Bytes(4, Bytes(9, Message(
            Unsigned(7, 234),
            Unsigned(8, 2),
            Unsigned(11, 42),
            Bytes(10, Message(
                Unsigned(6, 123),
                Unsigned(12, 55))))));

        ItemDetailObservation? item = SemanticDecoders.TryDecodeItemDetail(
            map.NormalizeBody("qcc", wire));

        item.Should().NotBeNull();
        item!.ItemUid.Should().Be(234);
        item.ItemId.Should().Be(42);
        item.Quantity.Should().Be(2);
        item.Stats.Should().ContainSingle()
            .Which.Should().Be(new ItemStatObservation(123, 55));
    }

    [Fact]
    public void ConfigOnlyCanRemapActualMarketSelectionFields()
    {
        ProtocolMap map = LoadMap(
            """
            {
              "market_selection_response": "qdd",
              "field_mappings": {
                "market_selection_response": {"2":7,"3":8}
              }
            }
            """);

        byte[] wire = Message(
            Unsigned(7, 9),
            Unsigned(8, 12851));

        SemanticDecoders.TryDecodeMarketSelectionItemId(
            map.NormalizeBody("qdd", wire))
            .Should().Be(12851);
    }

    [Theory]
    [InlineData("{\"price_list\":\"abc\",\"field_mappings\":{\"price_list\":{\"1\":2,\"3\":2}}}")]
    [InlineData("{\"price_list\":\"abc\",\"field_mappings\":{\"not_a_route\":{\"1\":1}}}")]
    [InlineData("{\"price_list\":\"abc\",\"field_mappings\":{\"price_list\":{\"1\":0}}}")]
    [InlineData("{\"price_list\":\"abc\",\"field_mappings\":{\"price_list\":{\"1\":{\"wire\":7,\"fields\":null}}}}")]
    public void InvalidProfilesAreRejectedAtLoadTime(string json)
    {
        Action load = () => LoadMap(json);
        load.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void UnexpectedNestedWireShapeFailsClosed()
    {
        ProtocolMap map = LoadMap(
            """
            {
              "price_list": "qaa",
              "field_mappings": {
                "price_list": {
                  "2": {"wire":7,"fields":{"2":2}}
                }
              }
            }
            """);

        // Field 7 is not a nested message, and cannot be safely translated.
        byte[] wire = Unsigned(7, 42);

        map.NormalizeBody("qaa", wire).Should().BeEmpty();
    }

    [Fact]
    public void AllProductionSemanticRoutesHaveOneSharedFieldMapping()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "NetworkFixtures",
            "protocol-map.json");

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        JsonElement layouts = root.GetProperty("field_mappings");
        root.GetProperty("schema_version").GetInt32().Should().Be(1);
        JsonElement verified = root.GetProperty("verified_routes");

        string[] routeNames =
        [
            "price_list", "market_selection_response", "crush_result",
            "item_detail", "workshop_slot_put", "purchase_request",
            "purchase_offer", "purchase_receipt", "inventory_add",
            "inventory_quantity", "inventory_remove", "craft_prepare",
            "craft_output", "smithmagic_request", "smithmagic_batch_request",
            "smithmagic_result", "smithmagic_aux",
            "market_listing_request", "market_listing_created"
        ];

        foreach (string route in routeNames)
        {
            root.GetProperty(route).GetString().Should().NotBeNullOrWhiteSpace();
            layouts.TryGetProperty(route, out _).Should().BeTrue(
                $"the {route} scanner route must remain in the shared profile");
            verified.TryGetProperty(route, out JsonElement status).Should().BeTrue();
            string? statusText = status.GetString();
            (statusText?.StartsWith("legacy-", StringComparison.Ordinal) == true ||
             statusText?.StartsWith("observed-", StringComparison.Ordinal) == true)
                .Should().BeTrue();
        }

        layouts.EnumerateObject().Count().Should().Be(routeNames.Length);
        verified.EnumerateObject().Count().Should().Be(routeNames.Length);
        ProtocolMap.Load(path).Should().NotBeNull();
    }

    private static ProtocolMap LoadMap(string json)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "BestCrush.Protocol-" + Guid.NewGuid().ToString("N") + ".json");

        try
        {
            File.WriteAllText(path, json);
            return ProtocolMap.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
