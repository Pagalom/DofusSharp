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
    Console.WriteLine("Pour rechercher un champ XP dans les réponses ipz mono-entrée après fabrication :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-xp-study");
    Console.WriteLine("Pour comparer deux métiers via les messages ipz mono-entrée :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-job-study");
    Console.WriteLine("  (P3g) --profession-job-study --check-public-job-ids : vérification locale des IDs publics et roster 20 lignes");
    Console.WriteLine("Pour comparer exactement une fenetre Dofus et son flux Npcap :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --window-flow-link");
    Console.WriteLine("Pour comparer anonymement les métiers entre connexions TCP :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-continuity");
    Console.WriteLine("Pour suivre ksc → ksb → ipz par session TCP, sans référence privée :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-flow");
    Console.WriteLine("Pour rechercher localement le nom/niveau dans les messages candidats :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --identity-name");
    Console.WriteLine("Pour comparer anonymement f3 entre deux personnages, dans deux connexions :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --identity-pair");
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

Console.WriteLine(options.WindowFlowLink
    ? "BestCrush Network Probe — P3k lien Windows/Npcap (sans decodage metier)"
    : $"BestCrush Network Probe — mapping de référence {map.ClientBuild} (build réel du jeu non vérifié)");
Console.WriteLine($"Interface : [{options.DeviceIndex}] {devices[options.DeviceIndex.Value].Name}");
Console.WriteLine($"Filtre    : tcp port {options.Port}");
if (options.WindowFlowLink)
{
    Console.WriteLine("P3k : quatre champs TCP compares localement aux sockets Windows et HWND/PID.");
    Console.WriteLine("Deux clients connectes, activite reseau sur chacun, puis Ctrl+C.");
    Console.WriteLine("Aucun titre, IP, port local, ou contenu de jeu dans les sorties.");
    Console.WriteLine("Compte/serveur restent non verifies; aucune hypothese sur le build du jeu.");
}
else if (options.CharacterScan)
{
    Console.WriteLine(options.ProfessionJobStudy
        ? "Mode P3f : étude locale des champs candidats métiers, sans affichage de valeurs."
        : options.ProfessionXpStudy
        ? "Mode P3e : étude locale des champs numériques des ipz mono-entrée, sans valeurs affichées."
        : options.ProfessionContinuity
        ? "Mode P3b : comparaison anonymisée des métiers avec le premier ipz de cette capture."
        : options.ProfessionFlow
        ? "Mode P3a : parcours ksc → ksb → ipz par connexion TCP, compte/serveur NON vérifiés."
        : options.IdentityPair
        ? "Mode P3m.3 : comparaison de f3 pour deux personnages, sans valeurs affichées."
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
    if (options.ProfessionXpStudy)
        Console.WriteLine("P3e : un seul client à la fois ; deux fabrications, noter XP après chacune. Références demandées après Ctrl+C.");
    if (options.ProfessionJobStudy)
        Console.WriteLine("P3f : un seul client ; deux métiers, deux crafts par métier, puis références locales après Ctrl+C.");
    if (options.CheckPublicJobIds)
        Console.WriteLine("P3g : démarrer avant la connexion au personnage pour capturer ipz à 20 lignes ; comparaison des IDs publics uniquement après vérification manuelle.");
    Console.WriteLine("Notes manuelles : connexion, caractéristiques, métiers (J), inventaire, banque, déconnexion.");
    if (options.IdentityName)
        Console.WriteLine("Recherche du nom exact en UTF-8 ; les chemins affichés ne prouvent pas l'identité du personnage.");
    if (options.IdentityFields)
        Console.WriteLine("ksb/ksc : recherche des champs égaux (sans leurs valeurs) dans une même session ; ID et identité NON vérifiés.");
    if (options.IdentityPair)
        Console.WriteLine("P3m.3 : deux références locales distinctes, deux sessions TCP et deux familles ksc/ksb par personnage. Ne jamais envoyer les références.");
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

CharacterIdentityPairStudy? identityPairStudy = null;
if (options.IdentityPair)
{
    Console.WriteLine();
    Console.WriteLine("Saisir deux références Nom:Niveau. Les noms peuvent être identiques si les niveaux diffèrent.");
    Console.WriteLine("Même nom ET même niveau : diagnostic impossible sans identité serveur/personnage vérifiée.");
    Console.WriteLine("Les noms, niveaux et valeurs candidates restent en RAM et ne sont jamais affichés.");
    Console.Write("Référence personnage A : ");
    string? rawA = Console.ReadLine();
    Console.Write("Référence personnage B : ");
    string? rawB = Console.ReadLine();

    if (!CharacterNameLevelReferenceInput.TryParse(rawA, out var first) ||
        !CharacterNameLevelReferenceInput.TryParse(rawB, out var second) ||
        (string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase) &&
         first.Level == second.Level))
    {
        Console.Error.WriteLine("Références invalides : Nom:Niveau valide requis ; deux couples nom+niveau identiques sont indiscernables.");
        return;
    }

    identityPairStudy = new CharacterIdentityPairStudy(first, second);
    rawA = null;
    rawB = null;
    Console.WriteLine("Deux références locales chargées. Connecter A, fermer complètement Dofus, puis connecter B, sans interrompre le probe.");
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

if (options.WindowFlowLink)
{
    using WindowTcpFlowLinkProbe probe = new(devices[options.DeviceIndex.Value], options.Port);
    probe.Start();
    stop.Wait();
    probe.Stop();
    probe.WriteSummary();
}
else if (options.CharacterScan)
{
    // A single opt-in in-RAM collector: only the first valid TCP session
    // can contribute numeric fields. Local reference input is AFTER capture.
    ProfessionXpWireStudy? xpWireStudy =
        options.ProfessionXpStudy || options.ProfessionJobStudy
            ? new ProfessionXpWireStudy(options.CheckPublicJobIds) : null;

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
        options.ProfessionContinuity,
        xpWireStudy,
        options.ProfessionJobStudy,
        identityPairStudy);

    probe.Start();
    if (options.IdentityPair)
    {
        Console.WriteLine("P3m.3 : capture démarrée, diagnostic TCP actif ; Ctrl+C pour arrêter.");
        Console.WriteLine("P3m.3 : un bilan anonyme sera affiché toutes les 10 secondes.");
        while (!stop.Wait(TimeSpan.FromSeconds(10)))
            probe.WriteTrafficStatus("live");
    }
    else
        stop.Wait();
    probe.Stop();

    if (options.IdentityPair)
        probe.WriteTrafficStatus("final");

    if (xpWireStudy is not null)
    {
        Console.WriteLine();
        Console.WriteLine($"P3{(options.ProfessionJobStudy ? "f" : "e")} : {xpWireStudy.Count} ipz mono-entrée retenus en RAM sur la première session valide.");

        if (options.ProfessionJobStudy)
        {
            if (xpWireStudy.Count >= 4)
            {
                Console.WriteLine("Saisir quatre références ou plus : index:Métier:Niveau:XP-après-fabrication.");
                Console.WriteLine("Au moins deux métiers distincts et deux observations par métier.");
                Console.WriteLine("Exemple FICTIF : 1:Sculpteur:6:390,2:Bricoleur:12:875,3:Sculpteur:6:420,4:Bricoleur:12:900");
                Console.WriteLine("Associer chaque index PROFESSION_JOB à sa fabrication, sans saisir de nom de personnage.");
                Console.WriteLine("Les références restent locales et ne sont pas recopiées dans le résultat.");
                // A typo in a local reference must not discard the capture.
                while (true)
                {
                    Console.Write("Références P3f (Entrée pour passer) : ");
                    string? inputJobs = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(inputJobs))
                        break;

                    if (!ProfessionJobReferenceInput.TryParse(
                            inputJobs, out IReadOnlyList<ProfessionJobReference> refs) ||
                        !ProfessionJobReferenceInput.Validate(refs, xpWireStudy.Count))
                    {
                        Console.WriteLine(
                            $"P3f : saisie invalide. Format index:Métier:Niveau:XP ; " +
                            $"indices existants : 1 à {xpWireStudy.Count} ; " +
                            "au moins deux références par métier, XP croissante par métier. " +
                            "Corriger ci-dessous sans relancer la capture.");
                        continue;
                    }

                    string comparison = xpWireStudy.CompareJobs(refs);
                    Console.WriteLine(comparison);
                    if (comparison.StartsWith(
                        "p3f=references-not-confirmed", StringComparison.Ordinal))
                    {
                        Console.WriteLine(
                            "P3f : niveau ou XP non concordants. Vérifier les relevés " +
                            "et les index ; une nouvelle saisie reste possible, " +
                            "ou Entrée pour terminer.");
                        continue;
                    }
                    if (options.CheckPublicJobIds)
                    {
                        bool? idsMatch = xpWireStudy.MatchPublicJobIds(refs);
                        Console.WriteLine("p3g=public-id-check match=" +
                            (idsMatch is null ? "unavailable" : idsMatch.Value ? "yes" : "no") +
                            " references=" + refs.Count +
                            " account=unverified server=unverified mapping=unverified");
                        Console.WriteLine(xpWireStudy.DescribePublicRoster(idsMatch == true));
                    }
                    break;
                }
            }
            else
                Console.WriteLine("P3f : au moins quatre ipz mono-entrée sont requis pour comparer deux métiers.");
        }
        else
        {
            if (xpWireStudy.Count >= 2)
            {
                Console.WriteLine("Saisir au moins DEUX références sous la forme index:niveau:XP-après-fabrication.");
                Console.WriteLine("Associer l'index affiché PROFESSION_XP à chaque fabrication ; ignorer les ipz sans relevé.");
                Console.WriteLine("Exemple fictif : 1:3:124,2:3:138");
                Console.WriteLine("La saisie est comparée localement, jamais recopiée dans les résultats.");
                // Keep ephemeral observations available after a typing error.
                while (true)
                {
                    Console.Write("Références P3e (Entrée pour passer) : ");
                    string? rawXpReferences = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(rawXpReferences))
                        break;

                    if (!ProfessionXpReferenceInput.TryParse(
                            rawXpReferences, out IReadOnlyList<ProfessionXpReference> refs) ||
                        refs.Any(r => r.Index > xpWireStudy.Count))
                    {
                        Console.WriteLine(
                            $"P3e : saisie invalide. Format index:niveau:XP ; " +
                            $"indices existants : 1 à {xpWireStudy.Count} ; " +
                            "XP strictement croissante. " +
                            "Corriger ci-dessous sans relancer la capture.");
                        continue;
                    }

                    Console.WriteLine(xpWireStudy.Compare(refs));
                    break;
                }
            }
            else
                Console.WriteLine("P3e : au moins deux ipz mono-entrée sont requis pour comparer les champs XP.");
        }
    }
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
    bool ProfessionXpStudy,
    bool ProfessionJobStudy,
    bool CheckPublicJobIds,
    bool WindowFlowLink,
    bool IdentityName,
    bool IdentityFields,
    bool IdentityPair,
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
        bool professionXpStudy = false;
        bool professionJobStudy = false;
        bool checkPublicJobIds = false;
        bool windowFlowLink = false;
        bool identityName = false;
        bool identityFields = false;
        bool identityPair = false;
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
                case "--profession-xp-study":
                    professionXpStudy = true;
                    break;
                case "--profession-job-study":
                    professionJobStudy = true;
                    break;
                case "--check-public-job-ids":
                    checkPublicJobIds = true;
                    break;
                case "--window-flow-link":
                    windowFlowLink = true;
                    break;
                case "--identity-name":
                    identityName = true;
                    break;
                case "--identity-fields":
                    identityFields = true;
                    break;
                case "--identity-pair":
                    identityPair = true;
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
            (professionXpStudy ? 1 : 0) +
            (professionJobStudy ? 1 : 0) +
            (characterShape ? 1 : 0) + (identityName ? 1 : 0) +
            (identityFields ? 1 : 0) + (identityPair ? 1 : 0) +
            (windowFlowLink ? 1 : 0);
        if (selectedModes > 1)
            throw new ArgumentException(
                "Use only one character diagnostic mode at a time.");

        if (checkPublicJobIds && !professionJobStudy)
            throw new ArgumentException("--check-public-job-ids requires --profession-job-study.");

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
                professionFlow || professionContinuity || professionXpStudy ||
                professionJobStudy || identityName || identityFields || identityPair,
            characterShape, shapeKeys, professionMatch, professionDecode,
            professionStudy, showCandidateJobIds, professionIdentify,
            professionFlow, professionContinuity, professionXpStudy,
            professionJobStudy, checkPublicJobIds, windowFlowLink,
            identityName, identityFields, identityPair, expectedClassId);
    }
}
