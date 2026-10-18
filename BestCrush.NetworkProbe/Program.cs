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
    Console.WriteLine("Pour vérifier f3/f6, f4 et les identifiants de métier candidats :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-study");
    Console.WriteLine("  (option explicite) --show-candidate-ids");
    Console.WriteLine("Pour identifier des métiers par leur niveau et XP totale uniquement :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-identify --show-candidate-ids");
    Console.WriteLine("Pour comparer anonymement les métiers entre connexions TCP :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-continuity");
    Console.WriteLine("Pour suivre ksc → ksb → ipz par session TCP, sans référence privée :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-flow");
    Console.WriteLine("Pour rechercher localement le nom/niveau dans les messages candidats :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --identity-name");
    Console.WriteLine("Pour comparer localement les champs numériques ksb et ksc dans une même connexion :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --identity-fields");
    Console.WriteLine("Pour vérifier le candidat de classe f5.f3 (comparaison anonymisée seulement) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --identity-fields --expected-class-id 6");
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
    Console.WriteLine(options.ProfessionContinuity
        ? "Mode P3b : comparaison anonymisée des métiers avec le premier ipz de cette capture."
        : options.ProfessionFlow
        ? "Mode P3a : parcours ksc → ksb → ipz par connexion TCP, compte/serveur NON vérifiés."
        : options.IdentityFields
        ? "Mode P2b : étude des champs voisins du nom/niveau dans ksb et ksc, par connexion TCP."
        : options.IdentityName
            ? "Mode P2a : recherche locale du nom et niveau dans les messages ciblés ; valeurs privées masquées."
        : options.ProfessionIdentify
            ? "Mode P1f : correspondance des métiers par niveau et XP totale, sans sauvegarde."
        : options.ProfessionStudy
            ? "Mode P1e : validation f3/f6, inventaire de f4, candidats f5 (valeurs masquées par défaut)."
        : options.ProfessionDecode
            ? "Mode P1d : ipz (20 lignes attendues), valeurs privées masquées, métiers non identifiés."
        : options.ProfessionMatch
            ? "Mode P1c : corrélation locale métier/niveau/XP, aucune valeur affichée ou sauvegardée."
        : options.CharacterShape
            ? "Mode P1b : formes protobuf uniquement pour les opcodes ciblés ; aucune valeur enregistrée."
            : "Mode P1a : métadonnées uniquement, flux TCP isolés, aucun payload enregistré.");
    if (options.CharacterShape)
        Console.WriteLine("Op codes ciblés : " + string.Join(", ", options.ShapeKeys.OrderBy(x => x)));
    if (options.ProfessionFlow)
        Console.WriteLine("Étude P3a en RAM seulement ; aucune référence privée, compte/serveur et mapping NON vérifiés.");
    if (options.ProfessionContinuity)
        Console.WriteLine("P3b : un seul client/compte/serveur à la fois. Égalité des métiers ≠ preuve d'identité ; aucune valeur enregistrée.");
    Console.WriteLine("Notes manuelles : connexion, caractéristiques, métiers (J), inventaire, banque, déconnexion.");
    if (options.IdentityName)
        Console.WriteLine("Recherche du nom exact en UTF-8 ; les chemins affichés ne prouvent pas l'identité du personnage.");
    if (options.IdentityFields)
        Console.WriteLine("ksb/ksc : recherche des champs égaux (sans leurs valeurs) dans une même session ; ID et identité NON vérifiés.");
    if (options.ExpectedClassId.HasValue)
        Console.WriteLine("P2i : comparaison locale de f5.f3 à la classe attendue ; aucune valeur réseau affichée, classe NON vérifiée.");
    if (options.ProfessionMatch)
        Console.WriteLine("Seuls les chemins de deux couples niveau/XP concordants au minimum seront affichés.");
    if (options.ProfessionDecode)
        Console.WriteLine("Seul ipz sera analysé. Les champs métiers non identifiés restent anonymes.");
    if (options.ProfessionIdentify)
    {
        Console.WriteLine("Seul ipz est analysé ; les références restent en RAM.");
        if (options.ShowCandidateJobIds)
            Console.WriteLine("ATTENTION : les numéros f5 affichés restent candidats, non validés comme IDs globaux.");
    }
    if (options.ProfessionStudy)
    {
        Console.WriteLine("Seul ipz est analysé ; aucun fichier n'est écrit.");
        if (options.ShowCandidateJobIds)
            Console.WriteLine("ATTENTION : affichage explicite des f5 CANDIDATS, qui ne sont pas encore prouvés être des IDs métier.");
    }
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

IReadOnlyList<ProfessionStudyReference>? studyReferences = null;
if (options.ProfessionStudy)
{
    Console.WriteLine();
    Console.WriteLine("Entrer 2 à 20 références : Métier:Niveau:XPtotale:XPdansNiveau:XPnecessaire.");
    Console.WriteLine("Séparer les métiers par une virgule. Exemple fictif : Alchimiste:82:30000:150:1640,Tailleur:63:19000:245:1260");
    Console.WriteLine("Références locales uniquement, non enregistrées.");
    Console.Write("Références P1e : ");
    string? rawStudy = Console.ReadLine();
    if (!ProfessionStudyReferenceInput.TryParse(rawStudy, out var parsedStudy))
    {
        Console.Error.WriteLine("Format P1e invalide : noms de métiers connus, 2 à 20 références distinctes et XP cohérentes.");
        return;
    }

    studyReferences = parsedStudy;
    rawStudy = null;
    Console.WriteLine("Références P1e chargées en RAM. Tu peux maintenant connecter ton personnage.");
}

IReadOnlyList<ProfessionNamedReference>? identifyReferences = null;
if (options.ProfessionIdentify)
{
    Console.WriteLine();
    Console.WriteLine("Saisir 2 à 20 références Métier:Niveau:XPtotale, séparées par une virgule.");
    Console.WriteLine("Exemple fictif : Cordomage:5:200,Façomage:4:120");
    Console.WriteLine("Données gardées uniquement en mémoire, jamais enregistrées.");
    Console.Write("Références P1f : ");
    string? rawIdentify = Console.ReadLine();
    if (!ProfessionNamedReferenceInput.TryParse(rawIdentify, out var parsedIdentify))
    {
        Console.Error.WriteLine("Saisie invalide : noms de métiers connus et couples distincts.");
        return;
    }

    identifyReferences = parsedIdentify;
    rawIdentify = null;
    Console.WriteLine("Références chargées. Connecter maintenant le personnage.");
}

CharacterNameLevelReference? characterNameReference = null;
if (options.IdentityName || options.IdentityFields)
{
    Console.WriteLine();
    Console.WriteLine("Saisir localement NomDuPersonnage:Niveau (exemple fictif : Explorateur:137).");
    Console.WriteLine("Ne pas saisir de nom de compte, mot de passe, jeton ni ID.");
    Console.WriteLine("Ces valeurs restent uniquement en mémoire, elles ne figurent pas dans les journaux.");
    Console.Write(options.IdentityFields ? "Référence P2b : " : "Référence P2a : ");
    string? inputIdentity = Console.ReadLine();
    if (!CharacterNameLevelReferenceInput.TryParse(
            inputIdentity, out CharacterNameLevelReference parsedIdentity))
    {
        Console.Error.WriteLine("Saisie invalide : fournir un nom de personnage et un niveau de 1 à 200.");
        return;
    }
    characterNameReference = parsedIdentity;
    inputIdentity = null;
    Console.WriteLine("Référence locale chargée. Connecter maintenant le personnage.");
}

using ManualResetEventSlim stop = new(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

if (options.CharacterScan)
{
    // P2a and P2b accept the same local input, but only one diagnostic
    // receives it. Never supply both character identity references.
    CharacterIdentityDiagnosticReferences diagnosticReferences =
        CharacterIdentityDiagnosticReferenceRouter.Route(
            options.IdentityName, options.IdentityFields, characterNameReference);

    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        options.CharacterShape ? options.ShapeKeys : null,
        professionReferences,
        options.ProfessionDecode,
        studyReferences,
        options.ShowCandidateJobIds,
        identifyReferences,
        diagnosticReferences.NameSearch,
        options.IdentityName ? ProbeOptions.IdentityNameOpCodes : null,
        diagnosticReferences.FieldStudy,
        options.ExpectedClassId,
        options.ProfessionFlow,
        options.ProfessionContinuity);

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
    bool ProfessionDecode,
    bool ProfessionStudy,
    bool ShowCandidateJobIds,
    bool ProfessionIdentify,
    bool ProfessionFlow,
    bool ProfessionContinuity,
    bool IdentityName,
    bool IdentityFields,
    int? ExpectedClassId)
{
    public static IReadOnlySet<string> IdentityNameOpCodes { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "krg", "ksb", "kes", "jev", "jgi", "ksc", "knc", "kod", "ifg", "kmz"
        };

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
        bool professionStudy = false;
        bool professionIdentify = false;
        bool professionFlow = false;
        bool professionContinuity = false;
        bool identityName = false;
        bool identityFields = false;
        int? expectedClassId = null;
        bool showCandidateJobIds = false;
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
                case "--profession-study":
                    professionStudy = true;
                    break;
                case "--profession-identify":
                    professionIdentify = true;
                    break;
                case "--profession-flow":
                    professionFlow = true;
                    break;
                case "--profession-continuity":
                    professionContinuity = true;
                    break;
                case "--identity-name":
                    identityName = true;
                    break;
                case "--identity-fields":
                    identityFields = true;
                    break;
                case "--expected-class-id" when i + 1 < args.Length &&
                    int.TryParse(args[i + 1], out int parsedClassId):
                    i++;
                    expectedClassId = parsedClassId;
                    break;
                case "--expected-class-id":
                    throw new ArgumentException(
                        "--expected-class-id requires a numeric class ID.");

                case "--show-candidate-ids":
                    showCandidateJobIds = true;
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

        int selectedModes =
            (professionMatch ? 1 : 0) + (professionDecode ? 1 : 0) +
            (professionStudy ? 1 : 0) + (professionIdentify ? 1 : 0) +
            (professionFlow ? 1 : 0) +
            (professionContinuity ? 1 : 0) +
            (characterShape ? 1 : 0) + (identityName ? 1 : 0) +
            (identityFields ? 1 : 0);
        if (selectedModes > 1)
            throw new ArgumentException(
                "Use only one character diagnostic mode at a time.");

        if (showCandidateJobIds && !professionStudy && !professionIdentify)
            throw new ArgumentException(
                "--show-candidate-ids requires --profession-study or --profession-identify.");
        if (expectedClassId.HasValue &&
            (!identityFields || expectedClassId.Value is < 1 or > 19))
            throw new ArgumentException(
                "--expected-class-id requires --identity-fields and an ID from 1 to 19.");

        // P1b/P1c both imply the isolated, passive capture path of P1a.
        return new ProbeOptions(
            device, port, all, list,
            characterScan || characterShape || professionMatch ||
                professionDecode || professionStudy || professionIdentify ||
                professionFlow || professionContinuity ||
                identityName || identityFields,
            characterShape, shapeKeys, professionMatch, professionDecode,
            professionStudy, showCandidateJobIds, professionIdentify,
            professionFlow, professionContinuity,
            identityName, identityFields, expectedClassId);
    }
}
