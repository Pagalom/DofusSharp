# Icônes locales des montures

Les tuiles et fiches Élevage utilisent les icônes d’objets du catalogue public
[dofusdude](https://api.dofusdu.de/dofus3/v1/fr/mounts/all), comme les autres
images locales DOFUS. Le relevé du 19 novembre 2026 contient 308 images et couvre
les 306 espèces élevables de `Resources/breeding-lineage.json`.

Le rapprochement utilise l’ID de l’espèce relevée dans Dofus, puis son `itemId`
dans la table des croisements. Le fichier est `img/dofus/mounts/{itemId}.png`.
Les couleurs et croisements ont ainsi leur propre image ; le nom traduit ou la
position dans la liste ne servent jamais à choisir un PNG. Si le catalogue des
noms est indisponible, l’ID d’espèce relevé suffit encore à résoudre l’image.

Installation ponctuelle, avec Python 3 sans bibliothèque supplémentaire :

```powershell
python .\scripts\install_mount_artwork.py
```

Le script vérifie la couverture de la table des espèces, l’origine des URL,
la signature et les dimensions des PNG, puis conserve la provenance et les
empreintes SHA-256 dans `manifest.json`. Les fichiers sont installés sous le
dossier `wwwroot/img/dofus/` déjà ignoré par Git. Leur chargement dans l’interface
est local ; aucun téléchargement de portrait n’est déclenché pendant l’affichage.
Une image absente conserve une illustration de secours sans icône cassée.

Les images restent soumises à la [notice Ankama](../NOTICE-RESSOURCES-ANKAMA.md).
Le garde-fou de compilation Release reste inchangé. Recompiler en Debug après
l’installation pour incorporer les PNG à la version locale.
