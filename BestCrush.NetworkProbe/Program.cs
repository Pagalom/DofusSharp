using BestCrush.Network.Capture;
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
    Console.WriteLine("Pour etudier les effets f7 des objets sur les messages courants iqx/iqz/kch (sans valeurs) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --item-effects-shape");
    Console.WriteLine("Pour comparer localement f5/f10 à des valeurs visibles en jeu, sans afficher les valeurs réseau :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --item-effects-compare");
    Console.WriteLine("Pour verifier si f10 est stable sur deux objets aux jets differents :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --item-effects-type-study");
    Console.WriteLine("Pour comparer f10 aux EffectId du catalogue DofusDb sur deux Kwaks (connexion API) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --item-effects-dofusdb-study");
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
    Console.WriteLine("Pour lier experimentalement les selections Connection aux fenetres et au chargement Game :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --window-server-link");
    Console.WriteLine("Pour investiguer ETW TCP Connect avec PID (Windows, autorisation admin probable) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --window-server-etw");
    Console.WriteLine("Pour comparer anonymement les métiers entre connexions TCP :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-continuity");
    Console.WriteLine("Pour rechercher des chemins numériques candidats de compte (A-X / A-Y / B-Z / A-X) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --account-wire-study");
    Console.WriteLine("Pour étudier les listes de personnages ksc/ksb comme groupes de compte candidats :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --account-roster-study");
    Console.WriteLine("Pour tester deux comptes SIMULTANÉS et une reconnexion B sans fermer A :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --account-roster-concurrent");
    Console.WriteLine("Pour joindre deux Game concurrents à leurs fenêtres Windows propriétaires :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --account-roster-window");
    Console.WriteLine("Pour suivre ksc → ksb → ipz par session TCP, sans référence privée :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --profession-flow");
    Console.WriteLine("Pour rechercher localement le nom/niveau dans les messages candidats :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --identity-name");
    Console.WriteLine("Pour comparer anonymement f3 entre deux personnages, dans deux connexions :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --identity-pair");
    Console.WriteLine("Pour chercher f3 dans d'autres familles réseau (candidats non vérifiés) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 6 --identity-cross-opcodes");
    Console.WriteLine("Pour étudier une transition de serveurs A → B → A (routes uniquement) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 6 --server-route-study");
    Console.WriteLine("Pour étudier les chemins protobuf candidats d'un serveur logique (A-B-A) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 6 --server-wire-study");
    Console.WriteLine("Pour contrôler kta:f1 sur différents personnages et serveurs :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 6 --kta-f1-control");
    Console.WriteLine("Pour comparer kta.f1 à l'ID candidat du personnage chargé ksb/ksc :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 6 --kta-character-compare");
    Console.WriteLine("Pour rechercher les noms des serveurs dans les messages de connexion (A-B-A) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 6 --server-name-study");
    Console.WriteLine("Pour vérifier le champ numérique de sélection sur le Connection Server (A-B-A) :");
    Console.WriteLine("  dotnet run --project BestCrush.NetworkProbe -- --device 3 --connection-selection-study");
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
if (options.ConcurrentRosterWindowStudy)
{
    Console.WriteLine("P3c.7 : P3c.6 + propriétaire OS unique du TCP Game (quadruplet → PID → HWND Dofus).");
    Console.WriteLine("Aucun PID, HWND, endpoint ou nom ne sera affiché, enregistré ni haché.");
    Console.WriteLine("Fenêtre OS n'est PAS identité de compte Ankama ni preuve du serveur jeu.");
}
if (options.ConcurrentRosterStudy)
{
    Console.WriteLine("P3c.6 : deux clients simultanés A et B puis reconnexion B (A reste ouvert).");
    Console.WriteLine("S→C:ksc/ksb seulement ; aucune valeur/nom/identifiant n'est affiché ou conservé sur disque.");
    Console.WriteLine("TCP distinct ne prouve ni compte Ankama ni serveur logique ; account=unverified.");
}
if (options.AccountRosterStudy)
{
    Console.WriteLine("P3c.5 : recherche d'un groupe de personnages candidat depuis S→C:ksc/ksb, jamais d'un identifiant Ankama.");
    Console.WriteLine("Quatre phases manuelles A-X / A-Y / B-Z / A-X, une seule fenêtre Dofus à la fois.");
    Console.WriteLine("Aucun nom ou identifiant réseau ne sera affiché, enregistré ni haché.");
}
if (options.AccountWireStudy)
{
    Console.WriteLine("P3c.4 : étude exploratoire UNIQUEMENT S→C:krg, 4 fenêtres successives, un seul client ouvert à la fois.");
    Console.WriteLine("Aucune lecture C→S d'authentification, aucun identifiant ou niveau affiché ni persisté.");
    Console.WriteLine("Les chemins candidats ne prouvent JAMAIS un compte ; account=unverified.");
}
if (options.ConnectionSelectionStudy)
{
    Console.WriteLine("P3m.11 : étude du protocole Connection distinct de type.ankama.com ; sélection numérique candidate A-B-A.");
    Console.WriteLine("Une seule instance Dofus par étape. Aucune valeur numérique, UUID ou jeton dans la sortie.");
}
if (options.ServerNameStudy)
{
    Console.WriteLine("P3m.9 : recherche bornée de deux noms publics de serveurs dans les messages TCP/5555.");
    Console.WriteLine("Les noms seront demandés en local ; aucun contenu de paquet ne sera affiché.");
    Console.WriteLine("Détection d'un nom ≠ preuve de serveur sélectionné ou du protocole de sélection.");
}
if (options.KtaCharacterCompare)
{
    Console.WriteLine("P3m.8 : contrôle expérimental kta.f1 versus ksb/ksc parent.f3, même serveur.");
    Console.WriteLine("Deux personnages distincts du même compte, une fenêtre à la fois ; valeur non affichée.");
}
if (options.KtaF1Control)
{
    Console.WriteLine("P3m.7 : contrôle ciblé C→S kta:f1 sur 4 étapes, même compte ; valeur non affichée.");
    Console.WriteLine("A/X → A/Y → B/Z → A/X, une fenêtre Dofus à la fois ; sémantique non vérifiée.");
}
if (options.ServerRouteStudy || options.ServerWireStudy)
{
    Console.WriteLine(options.ServerWireStudy
        ? "P3m.6 : comparaison des champs numériques TCP/5555 lors de A-B-A ; aucune identité de serveur vérifiée."
        : "P3m.5 : protocole manuel A-B-A, IP et ports TCP de jeu affichés ; serveur logique NON vérifié.");
    Console.WriteLine("Une instance Dofus à la fois. Chaque phase débute avant l'ouverture du client.");
}
if (options.WindowServerEtw)
{
    Console.WriteLine("P3m.14 : ETW TCP Connect + Npcap ; droits Windows eleves potentiellement requis.");
    Console.WriteLine("Demarrer AVANT les deux clients Dofus, garder les fenetres ouvertes jusqu'a Ctrl+C.");
    Console.WriteLine("Une session ETW unique sera liberee a l'arret ; aucune capture privee enregistre.");
    Console.WriteLine("Aucune correlation par simple succession TCP, compte ou serveur valide.");
}
if (options.WindowServerLink)
{
    Console.WriteLine("P3m.13 : liaison expérimentale Connection → PID/HWND → Game par quadruplet TCP.");
    Console.WriteLine("Démarrer AVANT les deux clients, garder les deux fenêtres ouvertes, puis Ctrl+C.");
    Console.WriteLine("Aucun tuple, PID, HWND, nom de personnage, jeton ou compte affiché.");
    Console.WriteLine("Références communautaires P3m.13 : Draconiros 295 / Kourial 355 ; aucun serveur validé en production.");
}
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
        : options.IdentityCrossOpcodes
        ? "Mode P3m.4 : recherche de chemins varint égaux à f3 dans d'autres opcodes (non vérifiés)."
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
        : options.ItemEffectsShape || options.ItemEffectsCompare ||
          options.ItemEffectsTypeStudy || options.ItemEffectsDofusDbStudy
            ? "Mode P3d.5 : structures internes f7 sur iqx/iqz/kch uniquement, valeurs et UID masqués."
            : "Mode P1a : métadonnées uniquement, flux TCP isolés, aucun payload enregistré.");
    if (options.CharacterShape)
        Console.WriteLine("Op codes ciblés : " + string.Join(", ", options.ShapeKeys.OrderBy(x => x)));
    if (options.ItemEffectsShape || options.ItemEffectsCompare ||
        options.ItemEffectsTypeStudy || options.ItemEffectsDofusDbStudy)
        Console.WriteLine("P3d.5 : capture un seul client ; affiche seulement les formes f7 et, si demandé, les champs correspondants, jamais les valeurs. Aucun contenu enregistré.");
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
    if (options.IdentityCrossOpcodes)
        Console.WriteLine("P3m.4 : une référence locale; observation numérique dans des messages ciblés, aucun ID ni serveur vérifié.");
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

CharacterCrossOpcodeFieldStudy? crossOpcodeStudy = null;
if (options.IdentityCrossOpcodes)
{
    Console.WriteLine();
    Console.WriteLine("Référence locale exacte Nom:Niveau du personnage à connecter :");
    Console.Write("Référence P3m.4 : ");
    string? rawCross = Console.ReadLine();
    if (!CharacterNameLevelReferenceInput.TryParse(rawCross, out var parsedCross))
    {
        Console.Error.WriteLine("Saisie invalide : référence Nom:Niveau requise.");
        return;
    }
    crossOpcodeStudy = new CharacterCrossOpcodeFieldStudy(parsedCross);
    rawCross = null;
    Console.WriteLine("Référence chargée en RAM. Se connecter puis se reconnecter au même personnage sans arrêter le probe.");
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

IReadOnlyList<long>? expectedEffectValues = null;
if (options.ItemEffectsCompare)
{
    Console.WriteLine("P3d.5 : entrer les valeurs EXACTES des effets non nuls de l'objet (1 à 16 valeurs), dans n'importe quel ordre.");
    Console.WriteLine("Exemple : 99,20. Ces références restent en RAM, jamais recopiées dans la sortie ni enregistrées.");
    Console.Write("Références d'effets locales : ");
    string? inputEffects = Console.ReadLine();
    string[] parts = inputEffects?.Split(',', StringSplitOptions.TrimEntries |
        StringSplitOptions.RemoveEmptyEntries) ?? [];
    long[] parsedEffects = new long[parts.Length];
    if (parts.Length is < 1 or > 16 ||
        parts.Where((part, i) => !long.TryParse(part, out parsedEffects[i])).Any())
    {
        Console.Error.WriteLine("Format invalide : 1 à 16 entiers séparés par des virgules.");
        return;
    }
    expectedEffectValues = parsedEffects;
    inputEffects = null;
    Console.WriteLine("Références chargées uniquement en RAM ; ouvrir Dofus après le début de capture.");
}

ItemEffectTypeStabilityStudy? effectTypeStudy = null;
if (options.ItemEffectsTypeStudy)
{
    Console.WriteLine("P3d.5 : comparer deux objets partageant les memes caracteristiques, mais avec des valeurs differentes.");
    Console.WriteLine("Objet A : entrer TOUTES ses valeurs non nulles dans l'ordre des caracteristiques communes (ex : 99,20).");
    Console.Write("Objet A : ");
    string? inputA = Console.ReadLine();
    Console.WriteLine("Objet B : commencer par les memes caracteristiques dans le MEME ordre, puis ajouter ses autres valeurs non nulles (ex : 24,26,5,3).");
    Console.Write("Objet B : ");
    string? inputB = Console.ReadLine();
    static long[]? ParseEffectTypeValues(string? input)
    {
        string[] parts = input?.Split(',', StringSplitOptions.TrimEntries |
            StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length is < 1 or > 16)
            return null;
        long[] result = new long[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], out result[i]) || result[i] == 0)
                return null;
        }
        return result;
    }
    long[]? first = ParseEffectTypeValues(inputA);
    long[]? second = ParseEffectTypeValues(inputB);
    inputA = null;
    inputB = null;
    if (first is null || second is null)
    {
        Console.Error.WriteLine("Format invalide : valeurs entieres non nulles separees par des virgules.");
        return;
    }
    try
    {
        effectTypeStudy = new ItemEffectTypeStabilityStudy(first, second);
    }
    catch (ArgumentException)
    {
        Console.Error.WriteLine("References invalides : A doit avoir 1-8 valeurs distinctes, B davantage (maximum 16). Les valeurs communes doivent etre alignees.");
        return;
    }
    Console.WriteLine("References en RAM uniquement. Capturer les deux objets dans UNE meme session TCP, dans l'ordre de votre choix.");
}

ItemEffectDofusDbCatalogStudy? effectCatalogStudy = null;
if (options.ItemEffectsDofusDbStudy)
{
    Console.WriteLine("P3d.5 : chargement des deux fiches publiques DofusDb (sans donnees de personnage)...");
    effectCatalogStudy = await DofusDbEffectCatalogReferenceLoader.TryLoadAsync();
    if (effectCatalogStudy is null)
    {
        Console.WriteLine("DOFUSDB=[catalogue-indisponible-ou-incompatible] : aucun diagnostic de correspondance lance.");
        return;
    }
    Console.WriteLine("DOFUSDB=[catalogue-charge] : comparateur de f10 pret pour Kwakanneau A / Kwakture B. Version du catalogue non verifiee.");
}

using ManualResetEventSlim stop = new(false);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Set();
};

if (options.WindowServerLink || options.WindowServerEtw)
{
    using WindowServerConnectionLinkProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        useEtw: options.WindowServerEtw);
    try
    {
        probe.Start();
    }
    catch (Exception ex) when (options.WindowServerEtw)
    {
        // The previous generic hint could not distinguish Windows elevation,
        // ETW provider activation and an unrelated Npcap startup failure.
        // Print only safe diagnostics: stage, exception type, HRESULT and
        // whether this process actually has an elevated administrator token.
        Exception cause = ex is WindowTcpEtwStartException etwFailure
            ? etwFailure.InnerException ?? ex
            : ex;
        string stage = ex is WindowTcpEtwStartException failure
            ? failure.Stage
            : "capture-or-other";
        string elevated = "unknown";
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            elevated = new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)
                ? "yes" : "no";
        }
        catch { /* Privilege inspection is diagnostic only. */ }

        Console.Error.WriteLine("p3m14=start-failed stage=" + stage +
            " reason=" + cause.GetType().Name +
            " hresult=0x" + cause.HResult.ToString("X8") +
            " elevated=" + elevated +
            " account=unverified server=unverified");
        return;
    }
    Console.WriteLine(options.WindowServerEtw
        ? "P3m.14 : ETW demarre ; ouvrir Draconiros et Kourial sur DEUX comptes puis revenir aux personnages sur Draconiros."
        : "P3m.13 : ouvrir une fenêtre Draconiros et une fenêtre Kourial sur DEUX comptes différents.");
    Console.WriteLine("Tester aussi une action réseau sur chaque fenêtre, puis Ctrl+C sans les fermer.");
    stop.Wait();
    probe.Stop();
    probe.WriteSummary();
}
else if (options.WindowFlowLink)
{
    using WindowTcpFlowLinkProbe probe = new(devices[options.DeviceIndex.Value], options.Port);
    probe.Start();
    stop.Wait();
    probe.Stop();
    probe.WriteSummary();
}
else if (options.ConnectionSelectionStudy)
{
    ConnectionServerSelectionStudy study = new();
    ServerRouteTransitionStudy route = new();
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        serverRouteStudy: route, connectionSelectionStudy: study);
    probe.Start();
    Console.WriteLine("P3m.11 : capture ouverte sur TCP/5555. A=Draconiros, B=Tylezia (choix MANUEL).");
    for (int stage = 0; stage < 3; stage++)
    {
        if (!study.StartStage(stage) || !route.StartStage(stage))
            break;
        Console.WriteLine(stage switch
        {
            0 => "PHASE A1 : lancer Dofus et connecter un personnage sur Draconiros.",
            1 => "PHASE B : lancer Dofus et connecter un personnage sur Tylezia.",
            _ => "PHASE A2 : relancer Dofus et revenir sur Draconiros."
        });
        Console.WriteLine("Fermer complètement Dofus, puis appuyer sur Entrée (Ctrl+C pour arrêter).");
        Task<string?> nextLine = Task.Run(() => Console.ReadLine());
        while (!nextLine.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (nextLine.IsCompleted && nextLine.Result is null))
            break;
    }
    probe.Stop();
    foreach (string line in route.Summarize())
        Console.WriteLine(line);
    foreach (string line in study.Summarize())
        Console.WriteLine(line);
    Console.WriteLine("P3m.11d : corrélation expérimentale par tentative TCP adjacente :");
    foreach (string line in study.SummarizeLinkedAttempts())
        Console.WriteLine(line);
    Console.WriteLine("P3m.12 : comparaison aux références communautaires de serveurs (295/293) :");
    foreach (string line in study.SummarizeReferenceChecks(
        expectedServerA: 295, expectedServerB: 293))
        Console.WriteLine(line);
}
else if (options.ServerNameStudy)
{
    Console.Write("Nom exact du serveur A : ");
    string? serverA = Console.ReadLine();
    Console.Write("Nom exact du serveur B (différent de A) : ");
    string? serverB = Console.ReadLine();
    ServerNameWireStudy study;
    try
    {
        study = new ServerNameWireStudy(serverA ?? "", serverB ?? "");
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine("P3m.9 : " + ex.Message);
        return;
    }
    serverA = serverB = null;
    ServerRouteTransitionStudy route = new();
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        serverRouteStudy: route,
        serverNameStudy: study);
    probe.Start();
    Console.WriteLine("P3m.9 : capture active. Dofus doit être fermé au début de chaque étape.");
    for (int stage = 0; stage < 3; stage++)
    {
        if (!study.StartStage(stage) || !route.StartStage(stage))
            break;
        Console.WriteLine(stage switch
        {
            0 => "PHASE A1 : se connecter sur le serveur A.",
            1 => "PHASE B : se connecter sur le serveur B.",
            _ => "PHASE A2 : se reconnecter sur le serveur A."
        });
        Console.WriteLine("Attendre le chargement du personnage, fermer complètement Dofus, puis Entrée (Ctrl+C pour arrêter).");
        Task<string?> nextLine = Task.Run(() => Console.ReadLine());
        while (!nextLine.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (nextLine.IsCompleted && nextLine.Result is null))
            break;
    }
    probe.Stop();
    Console.WriteLine("P3m.5 : destinations réseau des connexions de jeu observées :");
    foreach (string line in route.Summarize())
        Console.WriteLine(line);
    Console.WriteLine("P3m.9 : présence des noms A/B par opcode et chemin textuel (aucun ID vérifié) :");
    foreach (string line in study.Summarize())
        Console.WriteLine(line);
}
else if (options.KtaCharacterCompare)
{
    KtaSelectedCharacterStudy study = new();
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        ktaCharacterStudy: study);
    probe.Start();
    Console.WriteLine("P3m.8 : comparer deux personnages du MEME SERVEUR / MEME COMPTE.");
    for (int stage = 0; stage < 2; stage++)
    {
        if (!study.StartStage(stage))
            break;
        Console.WriteLine(stage == 0
            ? "PHASE X : connecter le personnage X, attendre le chargement, fermer entièrement Dofus."
            : "PHASE Y : connecter le personnage Y DIFFERENT, même compte et même serveur, puis fermer Dofus.");
        Console.WriteLine("Une fois le client totalement fermé, appuyer sur Entrée (Ctrl+C pour arrêter).");
        Task<string?> nextLine = Task.Run(() => Console.ReadLine());
        while (!nextLine.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (nextLine.IsCompleted && nextLine.Result is null))
            break;
    }
    probe.Stop();
    Console.WriteLine("P3m.8 : correspondances numériques candidates (jamais une preuve sémantique) :");
    foreach (string line in study.Summarize())
        Console.WriteLine(line);
}
else if (options.KtaF1Control)
{
    KtaFieldScopeStudy study = new();
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        ktaScopeStudy: study);
    probe.Start();
    Console.WriteLine("P3m.7 : capture active sur quatre connexions successives. Conserver le même compte.");
    for (int stage = 0; stage < 4; stage++)
    {
        if (!study.StartStage(stage))
            break;
        Console.WriteLine(stage switch
        {
            0 => "PHASE A-X1 : serveur A, personnage X (référence).",
            1 => "PHASE A-Y : même serveur A, personnage Y différent de X.",
            2 => "PHASE B-Z : serveur B, personnage Z, même compte.",
            _ => "PHASE A-X2 : serveur A, reprendre EXACTEMENT le personnage X initial."
        });
        Console.WriteLine("Se connecter, attendre le chargement, fermer complètement Dofus, puis Entrée (Ctrl+C pour arrêter).");
        Task<string?> nextLine = Task.Run(() => Console.ReadLine());
        while (!nextLine.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (nextLine.IsCompleted && nextLine.Result is null))
            break;
    }
    probe.Stop();
    Console.WriteLine("P3m.7 : contrôle ciblé kta:f1 (non identifié sémantiquement) :");
    foreach (string line in study.Summarize())
        Console.WriteLine(line);
}
else if (options.ServerRouteStudy || options.ServerWireStudy)
{
    ServerRouteTransitionStudy routeStudy = new();
    ServerWireTransitionStudy? wireStudy = options.ServerWireStudy
        ? new ServerWireTransitionStudy() : null;
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        serverRouteStudy: routeStudy,
        serverWireStudy: wireStudy);
    probe.Start();
    Console.WriteLine(options.ServerWireStudy
        ? "P3m.6 : capture ouverte. Comparaison numérique en RAM sans afficher les valeurs."
        : "P3m.5 : capture ouverte. Destinations du jeu affichées à la fin ; aucun jeton ni payload.");
    for (int stage = 0; stage < 3; stage++)
    {
        if (!routeStudy.StartStage(stage) ||
            (wireStudy is not null && !wireStudy.StartStage(stage)))
            break;
        Console.WriteLine(stage switch
        {
            0 => "PHASE A1 : connecter un personnage sur le serveur A, puis fermer Dofus complètement.",
            1 => "PHASE B : connecter un personnage sur le serveur B, puis fermer Dofus complètement.",
            _ => "PHASE A2 : reconnecter un personnage sur le serveur A, puis fermer Dofus complètement."
        });
        Console.WriteLine("Une fois le client fermé, appuyer sur Entrée pour terminer cette phase (Ctrl+C pour arrêter).");
        Task<string?> nextLine = Task.Run(() => Console.ReadLine());
        while (!nextLine.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (nextLine.IsCompleted && nextLine.Result is null))
            break;
    }
    probe.Stop();
    Console.WriteLine("P3m.5 : destinations IP:port de jeu observées (sans preuve de serveur logique) :");
    foreach (string line in routeStudy.Summarize())
        Console.WriteLine(line);
    if (wireStudy is not null)
    {
        Console.WriteLine("P3m.6 : candidats de champs numériques préchargement, sens NON vérifié :");
        foreach (string line in wireStudy.Summarize())
            Console.WriteLine(line);
    }
}
else if (options.ConcurrentRosterStudy || options.ConcurrentRosterWindowStudy)
{
    ConcurrentCharacterRosterStudy study = new();
    ConcurrentRosterWindowEvidenceStudy? windowStudy =
        options.ConcurrentRosterWindowStudy
            ? new ConcurrentRosterWindowEvidenceStudy() : null;
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        concurrentRosterStudy: study,
        windowEvidenceStudy: windowStudy);
    Console.WriteLine(options.ConcurrentRosterWindowStudy
        ? "P3c.7 : FERMER les deux clients Dofus avant de commencer."
        : "P3c.6 : FERMER les deux clients Dofus avant de commencer.");
    probe.Start();
    bool completed = true;
    for (int stage = 0; stage < 3; stage++)
    {
        if (!study.StartStage(stage))
        {
            completed = false;
            Console.WriteLine("p3c6=stage-rejected prerequisites=unmet account=unverified server=unverified");
            break;
        }
        Console.WriteLine(stage switch
        {
            0 => "ÉTAPE 1 : ouvrir A (personnage X), attendre ksc/ksb et GARDER A OUVERT. Puis Entrée.",
            1 => "ÉTAPE 2 : ouvrir B (personnage Z, même serveur), attendre ksc/ksb, FERMER B seulement (A reste ouvert). Puis Entrée.",
            _ => "ÉTAPE 3 : rouvrir B (MÊME personnage Z), attendre ksc/ksb, garder A et B ouverts, puis Entrée."
        });
        Task<string?> next = Task.Run(() => Console.ReadLine());
        while (!next.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (next.IsCompleted && next.Result is null))
        {
            completed = false;
            break;
        }
    }
    probe.Stop();
    if (!completed)
        Console.WriteLine("p3c6=stopped-early account=unverified server=unverified");
    foreach (string line in study.Summarize())
        Console.WriteLine(line);
    probe.WriteConcurrentWindowSummary();
    Console.WriteLine("P3c.6/P3c.7 : arrêter les clients Dofus après lecture du résultat.");
}
else if (options.AccountRosterStudy)
{
    CharacterRosterContinuityStudy study = new();
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        rosterStudy: study);
    Console.WriteLine("P3c.5 : fermer totalement TOUS les clients avant la première phase.");
    probe.Start();
    bool completed = true;
    for (int phase = 0; phase < 4; phase++)
    {
        if (!study.StartPhase(phase))
        {
            completed = false;
            break;
        }

        Console.WriteLine(phase switch
        {
            0 => "PHASE 1 A-X : compte A, personnage X sur le serveur commun.",
            1 => "PHASE 2 A-Y : même compte et même serveur, personnage Y DISTINCT de X.",
            2 => "PHASE 3 B-Z : compte B DISTINCT sur le MEME serveur, personnage Z.",
            _ => "PHASE 4 A-X : revenir au MEME personnage X du compte A."
        });
        Console.WriteLine("Attendre ksc et ksb, fermer totalement Dofus puis Entrée ; Ctrl+C pour arrêter.");
        Task<string?> read = Task.Run(() => Console.ReadLine());
        while (!read.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (read.IsCompleted && read.Result is null))
        {
            completed = false;
            break;
        }
    }
    probe.Stop();
    if (!completed)
        Console.WriteLine("p3c5=stopped-early account=unverified server=unverified");
    foreach (string line in study.Summarize())
        Console.WriteLine(line);
}
else if (options.AccountWireStudy)
{
    AccountWireFieldComparisonStudy study = new();
    using CharacterMetadataProbe probe = new(
        devices[options.DeviceIndex.Value], options.Port,
        accountWireStudy: study);
    probe.Start();
    Console.WriteLine("P3c.4 : fermer tous les clients Dofus avant la première phase.");
    bool allPhasesCompleted = true;
    for (int phase = 0; phase < 4; phase++)
    {
        if (!study.StartPhase(phase))
        {
            allPhasesCompleted = false;
            break;
        }

        Console.WriteLine(phase switch
        {
            0 => "PHASE 1 A-X : compte A, personnage X sur le serveur commun.",
            1 => "PHASE 2 A-Y : même compte A et même serveur, PERSONNAGE Y différent.",
            2 => "PHASE 3 B-Z : autre compte B sur le MEME serveur, personnage Z.",
            _ => "PHASE 4 A-X : reprendre exactement le personnage X initial du compte A."
        });
        Console.WriteLine("Attendre le chargement, fermer entièrement la fenêtre, puis appuyer sur Entrée (ou Ctrl+C).");
        Task<string?> nextLine = Task.Run(() => Console.ReadLine());
        while (!nextLine.IsCompleted && !stop.Wait(TimeSpan.FromMilliseconds(100))) { }
        if (stop.IsSet || (nextLine.IsCompleted && nextLine.Result is null))
        {
            allPhasesCompleted = false;
            break;
        }
    }
    probe.Stop();
    if (!allPhasesCompleted)
        Console.WriteLine("p3c4=stopped-early account=unverified server=unverified");
    foreach (string line in study.Summarize())
        Console.WriteLine(line);
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
        identityPairStudy,
        crossOpcodeStudy,
        itemEffectsShape: options.ItemEffectsShape || options.ItemEffectsCompare ||
            options.ItemEffectsTypeStudy || options.ItemEffectsDofusDbStudy,
        expectedEffectValues: expectedEffectValues,
        effectTypeStudy: effectTypeStudy,
        effectCatalogStudy: effectCatalogStudy);

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
    if (crossOpcodeStudy is not null)
    {
        Console.WriteLine("P3m.4 : pistes numériques sur la même connexion (égalité seule, sens inconnu) :");
        foreach (string line in crossOpcodeStudy.Summarize())
            Console.WriteLine(line);
    }

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
    bool WindowServerLink,
    bool WindowServerEtw,
    bool IdentityName,
    bool IdentityFields,
    bool IdentityPair,
    bool IdentityCrossOpcodes,
    bool ServerRouteStudy,
    bool ServerWireStudy,
    bool KtaF1Control,
    bool KtaCharacterCompare,
    bool ServerNameStudy,
    bool ConnectionSelectionStudy,
    int? ExpectedClassId,
    bool AccountWireStudy,
    bool AccountRosterStudy,
    bool ConcurrentRosterStudy,
    bool ConcurrentRosterWindowStudy,
    bool ItemEffectsShape,
    bool ItemEffectsCompare,
    bool ItemEffectsTypeStudy,
    bool ItemEffectsDofusDbStudy)
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
        bool itemEffectsShape = false;
        bool itemEffectsCompare = false;
        bool itemEffectsTypeStudy = false;
        bool itemEffectsDofusDbStudy = false;
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
        bool windowServerLink = false;
        bool windowServerEtw = false;
        bool identityName = false;
        bool identityFields = false;
        bool identityPair = false;
        bool identityCrossOpcodes = false;
        bool serverRouteStudy = false;
        bool serverWireStudy = false;
        bool ktaF1Control = false;
        bool ktaCharacterCompare = false;
        bool serverNameStudy = false;
        bool connectionSelectionStudy = false;
        bool accountWireStudy = false;
        bool accountRosterStudy = false;
        bool concurrentRosterStudy = false;
        bool concurrentRosterWindowStudy = false;
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
                case "--item-effects-shape":
                    itemEffectsShape = true;
                    break;
                case "--item-effects-compare":
                    itemEffectsCompare = true;
                    break;
                case "--item-effects-type-study":
                    itemEffectsTypeStudy = true;
                    break;
                case "--item-effects-dofusdb-study":
                    itemEffectsDofusDbStudy = true;
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
                case "--account-wire-study":
                    accountWireStudy = true;
                    break;
                case "--account-roster-study":
                    accountRosterStudy = true;
                    break;
                case "--account-roster-concurrent":
                    concurrentRosterStudy = true;
                    break;
                case "--account-roster-window":
                    concurrentRosterWindowStudy = true;
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
                case "--window-server-link":
                    windowServerLink = true;
                    break;
                case "--window-server-etw":
                    windowServerEtw = true;
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
                case "--identity-cross-opcodes":
                    identityCrossOpcodes = true;
                    break;
                case "--server-route-study":
                    serverRouteStudy = true;
                    break;
                case "--server-wire-study":
                    serverWireStudy = true;
                    break;
                case "--kta-f1-control":
                    ktaF1Control = true;
                    break;
                case "--kta-character-compare":
                    ktaCharacterCompare = true;
                    break;
                case "--server-name-study":
                    serverNameStudy = true;
                    break;
                case "--connection-selection-study":
                    connectionSelectionStudy = true;
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
            (characterShape ? 1 : 0) + (itemEffectsShape ? 1 : 0) +
            (itemEffectsCompare ? 1 : 0) +
            (itemEffectsTypeStudy ? 1 : 0) +
            (itemEffectsDofusDbStudy ? 1 : 0) +
            (identityName ? 1 : 0) +
            (identityFields ? 1 : 0) + (identityPair ? 1 : 0) +
            (identityCrossOpcodes ? 1 : 0) + (windowFlowLink ? 1 : 0) +
            (windowServerLink ? 1 : 0) +
            (windowServerEtw ? 1 : 0) +
            (serverRouteStudy ? 1 : 0) + (serverWireStudy ? 1 : 0) +
            (ktaF1Control ? 1 : 0) + (ktaCharacterCompare ? 1 : 0) +
            (serverNameStudy ? 1 : 0) +
            (connectionSelectionStudy ? 1 : 0) +
            (accountWireStudy ? 1 : 0) +
            (accountRosterStudy ? 1 : 0) +
            (concurrentRosterStudy ? 1 : 0) +
            (concurrentRosterWindowStudy ? 1 : 0);
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
            characterScan || characterShape || itemEffectsShape ||
                itemEffectsCompare || itemEffectsTypeStudy ||
                itemEffectsDofusDbStudy || professionMatch ||
                professionDecode || professionStudy || professionIdentify ||
                professionFlow || professionContinuity || professionXpStudy ||
                professionJobStudy || identityName || identityFields ||
                identityPair || identityCrossOpcodes,
            characterShape, shapeKeys, professionMatch, professionDecode,
            professionStudy, showCandidateJobIds, professionIdentify,
            professionFlow, professionContinuity, professionXpStudy,
            professionJobStudy, checkPublicJobIds, windowFlowLink,
            windowServerLink, windowServerEtw,
            identityName, identityFields, identityPair,
            identityCrossOpcodes, serverRouteStudy, serverWireStudy,
            ktaF1Control, ktaCharacterCompare, serverNameStudy,
            connectionSelectionStudy, expectedClassId, accountWireStudy,
            accountRosterStudy, concurrentRosterStudy,
            concurrentRosterWindowStudy, itemEffectsShape,
            itemEffectsCompare, itemEffectsTypeStudy,
            itemEffectsDofusDbStudy);
    }
}
