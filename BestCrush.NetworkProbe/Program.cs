using BestCrush.NetworkProbe.Capture;
using BestCrush.NetworkProbe.Protocol;
using SharpPcap;

Console.OutputEncoding = System.Text.Encoding.UTF8;

ProbeOptions options = ProbeOptions.Parse(args);
CaptureDeviceList devices = CaptureDeviceList.Instance;

if (options.ListDevices || options.DeviceIndex is null)
{
    Console.WriteLine("Interfaces de capture disponibles :");
    for (int i = 0; i < devices.Count; i++)
    {
        ICaptureDevice d = devices[i];
        Console.WriteLine($"  [{i}] {d.Name}");
        if (!string.IsNullOrWhiteSpace(d.Description))
            Console.WriteLine($"      {d.Description}");
    }

    if (options.ListDevices)
        return;

    Console.WriteLine();
    Console.WriteLine("Relance avec --device <index>. Exemple :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --all");
    return;
}

if (options.DeviceIndex < 0 || options.DeviceIndex >= devices.Count)
{
    Console.Error.WriteLine($"Index d'interface invalide : {options.DeviceIndex}.");
    return;
}

string mapPath = Path.Combine(AppContext.BaseDirectory, "protocol-map.json");
ProtocolMap map = ProtocolMap.Load(mapPath);

Console.WriteLine($"BestCrush Network Probe — Dofus {map.ClientBuild}");
Console.WriteLine($"Interface : [{options.DeviceIndex}] {devices[options.DeviceIndex.Value].Name}");
Console.WriteLine($"Filtre    : tcp port {options.Port}");
Console.WriteLine($"Mapping   : price_list={map.PriceList ?? "?"}, crush_result={map.CrushResult ?? "?"}");
Console.WriteLine();
Console.WriteLine("Ctrl+C pour arrêter.");
Console.WriteLine();

using DofusCaptureProbe probe = new(
    devices[options.DeviceIndex.Value],
    options.Port,
    map,
    options.ShowAllMessages);

using ManualResetEventSlim stop = new(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

probe.Start();
stop.Wait();
probe.Stop();

internal sealed record ProbeOptions(int? DeviceIndex, int Port, bool ShowAllMessages, bool ListDevices)
{
    public static ProbeOptions Parse(string[] args)
    {
        int? device = null;
        int port = 5555;
        bool all = false;
        bool list = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--device" when i + 1 < args.Length && int.TryParse(args[++i], out int parsedDevice):
                    device = parsedDevice;
                    break;
                case "--port" when i + 1 < args.Length && int.TryParse(args[++i], out int parsedPort):
                    port = parsedPort;
                    break;
                case "--all":
                    all = true;
                    break;
                case "--list":
                    list = true;
                    break;
            }
        }

        return new ProbeOptions(device, port, all, list);
    }
}
