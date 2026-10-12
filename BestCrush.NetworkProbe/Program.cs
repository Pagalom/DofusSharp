using BestCrush.NetworkProbe.Capture;
using BestCrush.Network.Protocol;
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
    Console.WriteLine("Pour étudier les personnages sans payload :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --character-scan");
    Console.WriteLine("Pour voir les structures de champs des messages de connexion :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --character-shape");
    Console.WriteLine("  (option) --shape-keys irl,ico,jei,isr");
    Console.WriteLine("Pour confronter les niveaux/XP visibles aux champs numériques :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-match");
    Console.WriteLine("Pour valider le décodage des 20 lignes ipz sans afficher leurs valeurs :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-decode");
    Console.WriteLine("Pour les diagnostics historiques :");
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
if (options.CharacterScan)
{
    Console.WriteLine(options.ProfessionDecode
        ? "Mode P1d : ipz (20 lignes attendues), valeurs privées masquées, métiers non identifiés."
        : options.ProfessionMatch
            ? "Mode P1c : corrélation locale métier/niveau/XP, aucune valeur affichée ou sauvegardée."
        : options.CharacterShape
            ? "Mode P1b : formes protobuf uniquement pour les opcodes ciblés ; aucune valeur enregistrée."
            : "Mode P1a : métadonnées uniquement, flux TCP isolés, aucun payload enregistré.");
    if (options.CharacterShape)
        Console.WriteLine("Op codes ciblés : " + string.Join(", ", options.ShapeKeys.OrderBy(x => x)));
    Console.WriteLine("Notes manuelles : connexion, caractéristiques, métiers (J), inventaire, banque, déconnexion.");
    if (options.ProfessionMatch)
        Console.WriteLine("Seuls les chemins de deux couples niveau/XP concordants au minimum seront affichés.");
    if (options.ProfessionDecode)
        Console.WriteLine("Seul ipz sera analysé. Les champs métiers non identifiés restent anonymes.");
}
else
{
    Console.WriteLine($"Mapping   : price_list={map.PriceList ?? "?"}, crush_result={map.CrushResult ?? "?"}");
}
Console.WriteLine();
Console.WriteLine("Ctrl+C pour arrêter.");
Console.WriteLine();

IReadOnlyList<ProfessionReference>? professionReferences = null;
if (options.ProfessionMatch || options.ProfessionDecode)
{
    Console.WriteLine();
    Console.WriteLine(options.ProfessionDecode
        ? "Saisir localement 2 à 20 couples niveau:XP TOTALE (séparés par une virgule)."
        : "Saisir localement 2 à 20 couples niveau:XP (séparés par une virgule).");
    Console.WriteLine("Exemple fictif : 82:150,63:245,47:20");
    Console.WriteLine("Ces valeurs ne seront pas affichées dans les résultats ni enregistrées.");
    Console.Write("Références : ");
    string? raw = Console.ReadLine();
    if (!ProfessionReferenceInput.TryParse(raw, out IReadOnlyList<ProfessionReference> parsed))
    {
        Console.Error.WriteLine("Saisie invalide : fournir entre 2 et 20 couples distincts niveau:XP, niveau > 1.");
        return;
    }

    professionReferences = parsed;
    raw = null;
    Console.WriteLine("Références chargées en mémoire. Démarrer la connexion du personnage.");
}

using ManualResetEventSlim stop = new(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

if (options.CharacterScan)
{
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        options.CharacterShape ? options.ShapeKeys : null,
        professionReferences,
        options.ProfessionDecode);

    probe.Start();
    stop.Wait();
    probe.Stop();
}
else
{
    using DofusCaptureProbe probe = new(
        devices[options.DeviceIndex.Value],
        options.Port,
        map,
        options.ShowAllMessages);

    probe.Start();
    stop.Wait();
    probe.Stop();
}

internal sealed record ProbeOptions(
    int? DeviceIndex,
    int Port,
    bool ShowAllMessages,
    bool ListDevices,
    bool CharacterScan,
    bool CharacterShape,
    IReadOnlySet<string> ShapeKeys,
    bool ProfessionMatch,
    bool ProfessionDecode)
{
    private static readonly string[] DefaultShapeKeys =
    [
        "kta", "kmz", "knc", "krg", "mdp", "ica", "irl",
        "ico", "jei", "isr", "irp", "hpe", "ifg", "lve",
        "lxb", "jfo", "jgq", "kmt", "kmi", "ipg", "ipz", "jhb"
    ];

    public static ProbeOptions Parse(string[] args)
    {
        int? device = null;
        int port = 5555;
        bool all = false;
        bool list = false;
        bool characterScan = false;
        bool characterShape = false;
        bool professionMatch = false;
        bool professionDecode = false;
        string? shapeKeysCsv = null;

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
                case "--character-scan":
                    characterScan = true;
                    break;
                case "--character-shape":
                    characterShape = true;
                    break;
                case "--profession-match":
                    professionMatch = true;
                    break;
                case "--profession-decode":
                    professionDecode = true;
                    break;
                case "--shape-keys" when i + 1 < args.Length:
                    shapeKeysCsv = args[++i];
                    characterShape = true;
                    break;
            }
        }

        HashSet<string> shapeKeys = new(
            shapeKeysCsv is null
                ? DefaultShapeKeys
                : shapeKeysCsv.Split(',', StringSplitOptions.TrimEntries |
                                            StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);

        if (shapeKeys.Count == 0 || shapeKeys.Count > 32 ||
            shapeKeys.Any(key => key.Length is < 1 or > 16 ||
                key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))))
            throw new ArgumentException(
                "--shape-keys requires 1 to 32 simple comma-separated opcodes.");

        if ((professionMatch && characterShape) ||
            (professionDecode && (professionMatch || characterShape)))
            throw new ArgumentException(
                "Use only one character diagnostic mode at a time.");

        // P1b/P1c both imply the isolated, passive capture path of P1a.
        return new ProbeOptions(
            device, port, all, list,
            characterScan || characterShape || professionMatch || professionDecode,
            characterShape, shapeKeys, professionMatch, professionDecode);
    }
}
