using System.Text.Json;
using BestCrush.Network.Protocol;
using FluentAssertions;
using static Tests.BestCrush.Network.NetworkPayload;

namespace Tests.BestCrush.Network;

public sealed class ProtocolWireNormalizerTest
{
    [Fact]
    public void MeanPriceAvailabilityUsesTheCentralOpcodeAndNestedFieldMapping()
    {
        var map = LoadMap("""
            { "market_mean_prices": "qmeans", "field_mappings": {
                "market_mean_prices": { "3": { "wire":9, "fields": { "1":7, "2":8 } } }
            } }
            """);
        var wire = Message(Bytes(9, Message(Unsigned(7, 33207), Unsigned(8, 373874))), Bytes(9, Unsigned(7, 33215)));
        var decoded = MarketMeanPriceDecoder.Decode(map.NormalizeBody("qmeans", wire));
        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.EntryCount);
        Assert.Contains(33207, decoded.WithMeanPrice);
        Assert.DoesNotContain(33215, decoded.WithMeanPrice);
    }

    [Fact]
    public void CurrentPublicRefreshUsesTheSharedPurchaseOfferFieldMapping()
    {
        var map = LoadMap("""
            { "purchase_offer_current": "qrefresh", "field_mappings": {
                "purchase_offer_current": { "1":7, "2":8, "3":9, "4":10 }
            } }
            """);
        var wire = Message(Bytes(7, Packed(346, 3970, 44882, 349980)),
            Unsigned(8, 200), Unsigned(9, 46), Unsigned(10, 5824));
        var decoded = CurrentPurchaseDecoder.DecodeStackableMarket(map.NormalizeBody("qrefresh", wire));
        decoded.Should().NotBeNull();
        decoded!.ItemId.Should().Be(200);
        decoded.Offers.Single().Ladder.Should().Equal(346UL, 3970UL, 44882UL, 349980UL);
    }

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

    [Fact]
    public void ManyFixedFieldsCanBeRemappedWithoutStackGrowthPerField()
    {
        ProtocolMap map = LoadMap(
            """
            {
              "market_selection_response": "qff",
              "field_mappings": {
                "market_selection_response": {"2": 7, "3": 8}
              }
            }
            """);

        const ulong fixedBits = 0x1122334455667788UL;
        static byte[] Fixed64(int field, ulong bits) =>
            Message(
                Varint(((ulong)field << 3) | 1),
                BitConverter.GetBytes(bits));

        byte[] wire = Message(Enumerable.Range(0, 16_384)
            .SelectMany(i => new[]
            {
                Float(7, i),
                Fixed64(8, fixedBits)
            })
            .ToArray());

        byte[] expected = Message(Enumerable.Range(0, 16_384)
            .SelectMany(i => new[]
            {
                Float(2, i),
                Fixed64(3, fixedBits)
            })
            .ToArray());

        map.NormalizeBody("qff", wire).Should().Equal(expected);
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
            "global_market_history_request", "global_market_history_response", "market_mean_prices", "personal_market_history",
            "breeding_paddock_snapshot_current",
            "breeding_stable_snapshot", "breeding_enclosure_request", "breeding_enclosure_response",
            "breeding_transfer_into_stable", "breeding_transfer_out_of_stable",
            "price_list", "market_selection_response", "crush_result",
            "crush_result_current", "item_detail", "item_detail_current",
            "workshop_slot_put", "purchase_request",
            "purchase_offer", "purchase_receipt", "purchase_request_current",
            "purchase_offer_current", "purchase_receipt_current", "inventory_add",
            "inventory_quantity", "inventory_remove", "craft_prepare",
            "craft_output", "smithmagic_request", "smithmagic_batch_request",
            "smithmagic_result", "smithmagic_aux",
            "market_listing_request", "market_listing_created",
            "market_listing_request_current", "market_listing_created_current"
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

    [Fact]
    public void CurrentPurchaseCodesAndFieldsCanChangeThroughTheSharedProfile()
    {
        var map = LoadMap("""
            {
              "purchase_request_current": "xaa",
              "purchase_offer_current": "xab",
              "purchase_receipt_current": "xac",
              "field_mappings": {
                "purchase_request_current": { "1": 7, "3": 8, "5": 9 },
                "purchase_offer_current": { "2": 7, "4": 8 },
                "purchase_receipt_current": { "2": 7, "3": 8 }
              }
            }
            """);
        var request = CurrentPurchaseDecoder.DecodeRequest(map.NormalizeBody(map.CurrentPurchaseRequest!,
            Message(Unsigned(7, 1), Unsigned(8, 51), Unsigned(9, 850))));
        var offer = CurrentPurchaseDecoder.DecodeOffer(map.NormalizeBody(map.CurrentPurchaseOffer!,
            Message(Unsigned(7, 42), Unsigned(8, 51))));
        var receipt = CurrentPurchaseDecoder.DecodeReceipt(map.NormalizeBody(map.CurrentPurchaseReceipt!,
            Message(Unsigned(7, 51), Unsigned(8, 1))));

        request.Should().Be(new PurchaseRequestObservation(850, 1, 51));
        offer!.ItemId.Should().Be(42);
        offer.OfferId.Should().Be(51);
        receipt.Should().Be(new PurchaseReceiptObservation(1, 51));
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
