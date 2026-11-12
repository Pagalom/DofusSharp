using BestCrush.Domain.Models;
using BestCrush.Services;

namespace Tests.BestCrush;

public sealed class ForgemagingSessionStoreTest
{
    [Fact]
    public void SessionsRemainIndependentByServerAndEquipmentAndPreserveUnknowns()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new ForgemagingSessionStore(path);
            var one = new ForgemagingSession { ItemId = 1, ItemName = "A", Lines = [new() { Characteristic = Characteristic.Wisdom, Minimum = 1, Maximum = 50, Target = 40 }] };
            store.Save("Kourial", one); one.Lines[0].Target = 99;
            store.Save("Tylezia", new() { ItemId = 1, ItemName = "B" });
            store.Save("Kourial", new() { ItemId = 2, ItemName = "C" });
            var saved = store.Get("KOURIAL", 1)!;
            Assert.Equal(40, saved.Lines.Single().Target); Assert.Null(saved.Sink); Assert.Null(saved.Lines.Single().Current);
            saved.Lines[0].Target = 100;
            Assert.Equal(40, store.Get("Kourial", 1)!.Lines.Single().Target);
            Assert.Equal("C", store.Get("Kourial")!.ItemName); Assert.Equal("B", store.Get("Tylezia")!.ItemName);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void CorruptSessionFileIsNeverOverwrittenBySaving()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{unfinished"); var store = new ForgemagingSessionStore(path);
            Assert.Throws<InvalidDataException>(() => store.Get("Kourial"));
            Assert.Throws<InvalidDataException>(() => store.Save("Kourial", new() { ItemId = 1 }));
            Assert.Equal("{unfinished", File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
