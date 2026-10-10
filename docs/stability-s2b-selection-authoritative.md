# S2b — Sélection du serveur faisant autorité

## Décision utilisateur

S2 (`ee90cf2`) a été validé sous Windows : compilation réussie, **233/233 tests**, et 6 vérifications fonctionnelles réussies.

L'utilisateur a ensuite explicitement rejeté la confirmation supplémentaire du serveur. **La sélection du serveur dans BestCrush suffit**. Une éventuelle erreur de choix est assumée par l'utilisateur.

## Comportement courant

- Aucun bouton, bannière ou approbation supplémentaire de serveur.
- Aucun serveur sélectionné : pas de persistance réseau.
- Dès qu'un serveur est sélectionné : capture automatique attribuée à ce serveur, y compris avec plusieurs connexions réseau détectées.
- Le changement de serveur génère une nouvelle génération (serveur + génération), invalide les anciens messages encore en queue, purge le dernier équipement réseau et réinitialise les assemblages TCP, ainsi que les corrélations UID/achat lors du premier message suivant.
- Le serveur attaché au message reste immuable. Une écriture déjà commencée lors d'un changement peut se terminer sous son **ancien** nom de serveur ; elle n'est jamais réétiquetée avec le nouveau serveur.
- Aucune vérification automatique de concordance entre le serveur choisi et la connexion Dofus : le protocole actuellement décodé ne permet pas cette vérification.
- En présence de plusieurs clients simultanés, leurs observations seront attribuées au serveur sélectionné ; les problèmes distincts de corrélation interconnexions sont hors de ce lot.

## Périmètre

Aucun changement de prix, coefficient, formule, migration, version, `main` ou release. Suppression de la bannière et de l'ancien verrou explicite, tout en conservant le contrôle de génération et le routage existants.

Le paramètre `currentServerState` devenu inutile dans `CrushSessionService` est retiré.

## Checkpoint Windows obligatoire

```powershell
git switch refactor/codebase-cleanup
git status --short
git pull --ff-only origin refactor/codebase-cleanup
git rev-parse --short HEAD

dotnet build .\BestCrush\BestCrush.csproj -f net10.0-windows10.0.19041.0
dotnet test .\Tests.BestCrush\Tests.BestCrush.csproj --logger "console;verbosity=normal"
```

Attendu : **233 tests** (les 6 tests S2 sont réécrits pour la nouvelle politique).

Contrôle manuel conseillé, avec sauvegarde/copie de base : démarrage sans bannière, capture automatique sur serveur A, changement vers B sans confirmation et capture immédiatement sur B, données de A toujours sur A, F7/F8/concassage fonctionnels.

Ne pas commencer S3 avant validation.
