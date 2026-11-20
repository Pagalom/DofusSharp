using System.Security.Cryptography;
using System.Text;
using BestCrush.Network.Protocol;

namespace BestCrush.Services;

// Local planning scope only; never used to associate a network connection or identify an account.
internal static class ProductionTrackingScope
{
    internal static string? For(CurrentDofusClientsState clients, DofusClientWindowKey? window, string server)
    {
        var client = clients.Clients.FirstOrDefault(c => c.Key == window && c.HasCharacterTitle);
        return client is null ? null : Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(server + "\n" + client.DisplayName)));
    }
}

internal sealed record ProductionListingEvidence(string Scope, long ItemId, ulong InstanceUid,
    int Level, string Variant, bool? Fertile);

internal sealed record ProductionListing(string Id, string Scope, long ItemId, ulong InstanceUid,
    int Level, string Variant, bool? Fertile, DateTimeOffset At)
{
    public long Price { get; init; }
}

internal sealed record ProductionSale(string Id, string ListingId, DateTimeOffset At);
internal sealed record ProductionReturn(string Id, string ListingId, DateTimeOffset At);

internal static class ProductionListingReader
{
    internal static PaddockMountReading? MountForPlanning(LiveStockItem item, string scope)
    {
        var evidence = FromStock(scope, item);
        if (evidence is not { Variant: "mount" } || BreedingLineage.Rides.Values.FirstOrDefault(r => r.ItemId == evidence.ItemId) is not { } ride)
            return null;
        // Inventory does not expose the paddock GUID. This local token is only for planning, never a game identity.
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(scope + ":inventory:" + item.Uid))[..16]);
        return new(id, evidence.Level, BestCrush.Domain.Services.BreedingMountExperience.RequiredAtLevel(evidence.Level), 0, null)
        { SpeciesId = ride.Id, Fertility = evidence.Fertile == true ? BreedingMountFertility.Fertile : BreedingMountFertility.Sterile };
    }

    internal static ProductionListingEvidence? FromStock(string scope, LiveStockItem item)
    {
        if (item.ItemId > long.MaxValue || item.Quantity == 0 || item.EffectWire is not { Length: > 0 } effects) return null;
        foreach (ulong category in new ulong[] { 18, 121 })
            if (PetMarketDecoder.Read(effects, category) is { } pet)
                return new(scope, (long)item.ItemId, item.Uid, pet.Level, pet.Variant, null);

        var decoded = effects.Select(bytes => ProtoWire.ReadFields(bytes)).ToArray();
        if (decoded.Any(e => e is null)) return null;
        var levels = decoded.Where(e => Number(e!, 10) == 2821).ToArray();
        if (levels is not [var levelEffect]) return null;
        var payloads = levelEffect!.Where(f => f.Number == 9).ToArray();
        if (payloads is not [{ Bytes: { } bytes }] || ProtoWire.ReadFields(bytes) is not { } payload ||
            Number(payload, 1) is not (331 or 332 or 333) || Number(payload, 4) is not (>= 1 and <= 200 and var level)) return null;
        return new(scope, (long)item.ItemId, item.Uid, (int)level, "mount", decoded.Any(e => Number(e!, 10) == 3830));
    }

    private static ulong? Number(List<ProtoField> fields, int number) => fields.Where(f => f.Number == number).ToArray()
        is [{ WireType: ProtoWireType.Varint } field] ? field.Varint : null;
}
