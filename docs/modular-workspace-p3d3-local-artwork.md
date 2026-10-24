# BestCrush — images locales des métiers et portraits de classes (P3d.3)

## Origine / droits

Images issues du client **DOFUS 3.7.12.12** (publication du 20 octobre 2026),
réassemblées par la communauté via le projet dofusdude :
https://github.com/dofusdude/dofus3-main/releases/tag/3.7.12.12

Archives retenues :
- `job_images_64.tar.gz` — SHA-256 `79f495b081b8ed1ba38977ac2fb5798c6f0ea9bafa5d3858d4991deb44c6bd10`
- `class_head_images_64.tar.gz` — SHA-256 `9199f502a2a13070024730ffdcc5ec27ab42bb82daea80a4eca26a06d2134258`

**Droits des images : Ankama.** Leur extraction et leur usage local
ne constituent pas une autorisation de redistribution. Les PNG ne sont
pas publiés dans le dépôt. Avant de distribuer un installeur contenant
ces fichiers, clarifier les droits applicables.

## Installation ponctuelle (Windows)

Depuis la racine du dépôt, dans PowerShell :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install_dofus_artwork.ps1
```

Le script utilise les URL du tag 3.7.12.12, vérifie la somme SHA-256
de **chaque archive**, n'accepte que les PNG dont la signature est valide
et récupère exactement les 20 métiers du référentiel P3g.1 et 19 classes
(la classe Forgelance utilise l'identifiant d'illustration 20).

L'importeur lit désormais le fichier `jobs.json` de la même version
(empreinte SHA-256 contrôlée : `7340c83f7f0cd75aecaccf4bc6113e9519d507acd007488d4ee7a873bbaab0e6`).
Il utilise `id` pour identifier le métier et `iconId` pour retrouver
le PNG original `{iconId}-64.png` : le métier Forgeron (`id=11`) utilise
notamment `iconId=10`. Les images installées sont renommées selon
l'ID du métier, pour conserver les chemins internes de BestCrush.

Si une icône ou un portrait n'est pas présent dans l'archive, le script
installe les autres et affiche un avertissement. L'interface conserve
alors son visuel provisoire pour cet élément.

Les images sont enregistrées sous :
- `BestCrush/wwwroot/img/dofus/jobs/{publicJobId}.png`
- `BestCrush/wwwroot/img/dofus/classes/{classArtworkId}.png`

Après installation, **recompiler/redémarrer BestCrush**. Le système
standard des fichiers wwwroot embarque ces PNG dans le package de
l'application ; l'application ne télécharge jamais d'images de classes
ou de métiers en cours d'utilisation.

L'opération de téléchargement est manuelle et ponctuelle. Sans les PNG,
les émojis des métiers et les avatars génériques restent utilisables.
Le chargement local n'interfère pas avec la capture réseau, la base
SQLite, les profils temporaires ou la détection du serveur.

## Distinction importante

Le portrait est **un visage par défaut de la classe**, pas la tête
effective du personnage équipé/personnalisé. Aucun champ réseau ne
permet actuellement d'identifier son sexe, ses couleurs ou son skin.
Ne pas inférer l'apparence d'un personnage via son pseudo.

## Vérification

```powershell
git fetch origin feature/modular-workspace
git merge --ff-only FETCH_HEAD
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install_dofus_artwork.ps1
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj -m:1 -nr:false
dotnet run --project .\BestCrush\BestCrush.csproj -c Debug -f net10.0-windows10.0.19041.0
```

Ouvrir **Personnages** : vérifier une icône métier et le portrait de
classe sur la fiche, puis les mini-portraits en haut à droite.
Couper Internet après le lancement permet un contrôle complémentaire,
sans être une étape nécessaire.

L'import dépend ponctuellement de GitHub et ne peut pas fonctionner
sans Internet lors de sa toute première exécution. **L'application,
elle, utilise exclusivement les images locales.**
