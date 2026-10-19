# P3g — Vérifier une table publique d'ID métier (lecture seule)

## Source et limites

Fichier communautaire [Jobs.json](https://www.frigost.dev/API/Dofus%203.0/Data/Jobs.json),
répertoire daté du 9 octobre 2026 : 23 associations publiques entre
noms et ID, dont Base=1, Bestiologue=78, Éleveur=79.

Les études BestCrush antérieures ont observé 20 lignes dans les
messages `ipz` complets de Dofus 3.6.11.15.
On ne peut donc pas considérer sans test que les 20 identifiants
historiquement retenus sont exhaustifs ni que leurs valeurs sont
identiques à celles du champ réseau `f1.f5`.

La référence de 20 utilisée pour la **comparaison expérimentale**
est : Bûcheron, Forgeron, Sculpteur, Cordonnier, Bijoutier, Mineur,
Alchimiste, Tailleur, Paysan, Pêcheur, Chasseur, Forgemage,
Sculptemage, Façonneur, Cordomage, Joaillomage, Costumage,
Bricoleur, Façomage et Parchomage. Elle n'est PAS promue dans
le mapping de production.

## Expérience contrôlée

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --profession-job-study --check-public-job-ids
```


1. Lancer le probe avant de connecter son personnage, avec **un seul
   client** et sans changer de connexion TCP ; ouvrir l'interface métiers.
2. Vérifier la capture d'un `p3g=roster-captured rows=20` (si le
   serveur émet bien le snapshot complet pendant cette capture).
3. Faire deux crafts gagnant de l'XP sur chacun de deux métiers
   (par exemple Sculpteur et Cordonnier), relever les indices,
   niveaux et XP après chaque fabrication.
4. Après `Ctrl+C`, saisir les quatre références
   `index:Métier:Niveau:XP` dans l'invite.
5. Partager uniquement les lignes `p3f=...` et `p3g=...`.

P3g vérifie d'abord la correspondance niveau/XP, puis compare en RAM
les valeurs `f1.f5` aux IDs publics de ces deux métiers.
Le retour `p3g=public-id-check match=yes` permet au diagnostic
de compter les IDs de référence **dans le snapshot de 20 lignes**,
ainsi que les autres IDs publics et ceux restés inconnus.
Le diagnostic affiche les **noms publics** manquants / ajoutés,
mais jamais un ID extrait du réseau.

Si `match=no`, `match=unavailable` ou si le roster manque,
la table ne doit pas être activée automatiquement.
Un résultat positif confirme une concordance LOCALE uniquement :
pas de validation du compte, du serveur, des autres sessions ou
de l'exhaustivité de la taxonomie de cette version.
Aucune donnée n'est persistée. Ni `main`, ni la production
BestCrush, ni le fichier de mapping versionné ne sont modifiés.

## Validation Windows

```powershell
dotnet build .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj
```


Tests synthétiques de conformité des ID, désaccord de niveau/XP,
roster complet/incomplet, isolation TCP et absence d'ID réseau
dans les sorties. Ne pas mener de tests multicomptes.


## P3g.1 — Capture réelle, 20 ID connus, 2 correspondances directes

**Date : 19 octobre 2026 UTC (20 octobre, heure de Paris).**
Dofus 3.6.11.15 ; première session TCP valide ; capture démarrée
avant la connexion au personnage ; `ipz` complet de 303 octets,
20 lignes, suivi de quatre messages mono-entrée de 15 octets chacun.

Le comparateur local a confirmé : `level-match=yes xp-match=yes`,
`candidate-paths=[f1.f5]` et `candidate-status=single-candidate`.
La vérification des ID publics a rendu `p3g=public-id-check match=yes`
sur **Cordonnier (15) et Sculpteur (13)**, deux observations de
progression XP par métier. Aucun niveau ni compteur d'XP privé
n'est conservé dans ce document.

Le roster complet a rendu, relativement à **l'ancienne référence P3g** :

```text
rows=20 unique-f5=yes reference20-present=19/20 missing-public-names=[Parchomage] extra-public-names=[Éleveur] unknown-id-count=0 missing-f5-count=0
```


Ces vingt identifiants sont tous distincts et tous présents dans la
nomenclature publique de 23 ID. Le jeu de 20 ID observé est
**exactement** l'ensemble de référence après remplacement de
Parchomage (75) par Éleveur (79), déjà indiqué dans le fichier
versionné `ipz-profession-mapping.3.6.11.15.json`.

Le référentiel d'essai P3g a été corrigé en conséquence.
**Le résultat historique 19/20 n'était pas un échec de capture** :
c'était une divergence entre l'ancienne liste de test et
les 20 ID effectivement présents.

**Portée de la validation :**
- deux correspondances directes nom ↔ valeur `f1.f5` pendant cette session ;
- la présence des 20 valeurs publiques attendues sur le réseau ;
- les 18 autres noms s'appuient sur la nomenclature publique et les
  corrélations P1 antérieures documentées dans le dépôt, sans
  nouvelle comparaison de craft individuelle en P3g.1.

Aucune identité compte/serveur n'a été validée. On ne lie pas les
données de métiers à un personnage. Aucun mapping runtime de BestCrush
n'est activé et `professionId` reste nul dans le fichier
de recherche jusqu'à autorisation et qualification de l'usage ciblé.

La ligne partagée par l'opérateur concaténait textuellement
`unknown-id-count=0missing-f5-count=0`, mais le code examiné
contient déjà un espace entre les deux champs. Un test protège
maintenant ce séparateur ; sans reproduction, aucune cause
technique n'est attribuée.
