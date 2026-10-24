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

L'importeur recherche les noms de fichiers originaux avec leur suffixe
de résolution : `2-64.png` pour le métier Bûcheron et `70-64.png`
pour le portrait de classe Éniripsa. C'est le format produit par la
chaîne de préparation `doduda` ; les images installées sont ensuite
renommées en `2.png` / `7.png` pour les chemins internes BestCrush.

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
