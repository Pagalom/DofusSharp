# P3m.10 — Connexions TCP hors 5555 pendant la sélection de serveur

## Hypothèse

P3m.9 n'a trouvé **aucun nom** `Draconiros` ni
`Tylezia` dans 96 messages Ankama analysés sur
TCP/5555 (32 par phase A1/B/A2, `coverage=bounded`).
Ce résultat ne confirme ni l'absence de nom dans
d'autres protocoles, ni l'absence d'un identifiant
numérique de serveur.

**Question** : le client ou launcher contacte-t-il
des destinations distinctes de TCP/5555 pour sélectionner
le serveur, récupérer un catalogue ou préparer la
connexion au jeu ?

## Mode opératoire

Sur Windows, depuis la racine de DofusSharp :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\probe_dofus_all_ports.ps1
```

Le script **n'exige ni build ni dotnet**. Il utilise
`Get-Process`, `Get-NetTCPConnection` et un
`Start-Job` éphémère pour chacun des trois essais.

1. Fermer complètement tous les clients Dofus.
2. **A1 / Draconiros** : ouvrir Dofus, se connecter au
   personnage, attendre son chargement, fermer le client,
   puis appuyer sur Entrée dans PowerShell.
3. **B / Tylezia** : même manipulation sur Tylezia.
4. **A2 / Draconiros** : revenir sur Draconiros et
   fermer avant Entrée.
5. Transmettre les lignes `p3m10=`.

Le script attend deux secondes après avoir lancé sa tâche
de surveillance avant d'inviter l'utilisateur à démarrer
chaque phase. L'index de carte Npcap n'est pas nécessaire.

## Sorties

Les lignes `p3m10=endpoint` indiquent :

- phase déclarée A1/B/A2 ;
- nom du **processus propriétaire** et PID local ;
- adresse IP distante et port TCP distant ;
- instant UTC de première observation et état du socket.

Les lignes `p3m10=stage-...` indiquent le nombre de
connexions et destinations distinctes retenues ainsi
qu'une éventuelle limite atteinte. Le bilan
`p3m10=summary` donne le nombre de destinations
TCP présentes dans les deux phases A et celles
non vues en B, après comparaison du triplet
nom du processus / IP distante / port TCP.

Pour adapter le sondage et la limite de sortie :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\probe_dofus_all_ports.ps1 -PollMilliseconds 300 -MaxEventsPerPhase 160
```

## Limites et fiabilité

- Le script **ne capture aucun contenu réseau**
  et ne lit aucun jeton, identifiant de compte
  ou donnée TLS. Il ne crée aucun fichier.
- L'IP/port distant est montré : information
  technique autorisée pour ce diagnostic.
- Seuls les processus dont le nom contient
  `dofus` ou `ankama` sont étudiés.
  Un processus enfant avec un autre nom peut
  héberger une connexion que le script ne verra pas.
- La table TCP Windows est sondée
  périodiquement ; un socket très court peut
  apparaître et disparaître entre deux sondages.
- Les destinations **UDP/QUIC**, DNS émis par le
  résolveur système, connexions du navigateur,
  autres ports d'un proxy ou processus d'authentification
  non reconnus ne sont pas observés.
- Même une destination distincte entre A et B
  ne prouve **pas** qu'elle transporte le choix
  du serveur : les connexions peuvent servir à
  des fonctions non liées (télémétrie, mises à jour).
- Aucune identité de compte ou serveur n'est inférée :
  `server=unverified` reste la règle.
- Les données appartiennent à la phase manuelle
  du probe, pas à la validation de serveur de
  BestCrush. La sélection utilisateur reste autoritaire.

## Étape d'après

Si le test fait apparaître des destinations ou
ports nouveaux, isoler le processus/flux, puis
déterminer si ce trafic est du TLS (et donc
illisible directement) ou un protocole ouvert
et s'il contient une sélection ou un catalogue.
Sinon, ne pas conclure qu'il n'existe aucun
autre canal ; privilégier un mécanisme plus
fiable d'observation des connexions et/ou
la recherche de métadonnées de sélection côté client.

Pas de mise à jour métier, de profil ou
de release à ce stade.
