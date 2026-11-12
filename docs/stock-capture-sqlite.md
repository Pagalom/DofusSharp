# Captures de stocks dans une base SQLite locale

Option explicite ajoutée le 25 octobre 2026 :

```powershell
dotnet run --project .\BestCrush.NetworkProbe\BestCrush.NetworkProbe.csproj -- --device 3 --stock-snapshot-study --stock-db stock-captures.db
```

Vérifier l'interface avec `--list`. Démarrer avant la connexion du personnage pour recevoir `irl`, puis ouvrir la banque pour recevoir `irp`. Arrêter par Ctrl+C.

Cette option conserve localement les identifiants candidats d'objets, UID, quantités, effets numériques compris et entrées d'effets protobuf exactes (base64 JSON). Les données ne sont pas envoyées à un service et ne figurent pas dans les rapports console. Le mode sans cette option reste en RAM. `bestcrush.db` et les bases étrangères sont refusées.

`stock_snapshots` contient une ligne par snapshot reçu, sa date UTC, le conteneur, un identifiant unique de lancement et le numéro de session TCP. `stock_items` contient toutes les lignes des snapshots CompleteCandidate, y compris les objets avec effets inconnus. Les entiers candidats sont stockés en texte décimal pour préserver toute la plage UInt64. Un effet inconnu reste NULL dans effects_json ; effect_wire_json conserve ses octets lorsque le décodeur borné les a acceptés. Un champ wire NULL signale une capture d'effets non disponible, pas l'absence d'effets.

Il s'agit d'un historique de captures candidates, pas encore d'un inventaire courant synchronisé. Chaque snapshot est écrit dans une transaction atomique. Les captures malformées/incomplètes sont enregistrées sans lignes utilisables ; EmptyCandidate reste un statut distinct. Les deltas de mouvements ne modifient pas ces captures. Ne pas choisir arbitrairement le dernier snapshot complet en ignorant une capture invalide ultérieure. Ne pas additionner les captures successives ou les sessions. Le compte et le serveur restent non vérifiés.

Exemple de lecture d'une capture choisie explicitement après contrôle de son statut :

```sql
SELECT id, run_id, session, container, captured_utc, status, source_rows, ownership
FROM stock_snapshots ORDER BY id DESC;
SELECT item_id_candidate, uid_candidate, quantity_candidate, effects_json, effect_issue
FROM stock_items WHERE snapshot_id = :snapshot_id ORDER BY ordinal;
```

Prochaines validations réelles : banque complète comparée à l'écran, inventaire capturé à la connexion, cohérence des quantités et rattachement du compte/serveur. L'intégration au calcul de craft/concassage nécessite ces validations et la synchronisation des mouvements. Les tests synthétiques ne remplacent pas ces observations en jeu.

## Validation réelle du 25 octobre 2026

Compilation réussie et 1 345 tests réussis (dont 5 tests SQLite). Une ouverture de banque via l'interface Dofus 3.7.12.12 a produit un irp CompleteCandidate de 560 lignes : 560 lignes persistées, 60 avec effets numériques inconnus, zéro ligne sans copie brute des effets. PRAGMA integrity_check : ok. La capture a commencé après connexion : aucun snapshot inventaire reçu à ce stade. Cette observation ne valide pas encore le sens de tous les champs ni le rattachement compte/serveur.


Reconnexion pilotée par interface le même jour : irl CompleteCandidate, 503 lignes inventaire persistées (session 3, banque précédente session 1). 112 lignes sans effets numériques décodés ; une ligne sans copie brute car le plafond de 64 effets est dépassé (too-many-effects). Les 503 identifiants/quantités candidats sont conservés. Intégrité SQLite : ok. Les sessions restent distinctes ; aucun rattachement automatique n'a été ajouté.


## Conservation des grandes listes d'effets

Le plafond de conservation brute passe à 1 024 entrées, avec les limites existantes de 8 Kio par ligne, 1 Kio par effet et 256 Kio par snapshot. Le décodeur numérique garde son plafond de 64 : aucune nouvelle sémantique n'est supposée. Trois tests couvrent 65, 1 024 et 1 025 entrées ; total 1 348 tests réussis.

Nouvelle capture réelle : 503 lignes inventaire et 560 lignes banque dans le même lancement/session TCP, zéro ligne sans effets bruts. L'objet précédemment bloqué contient 105 effets. Le regroupement structurel est maintenant candidat pour les deux conteneurs ; les effets numériques inconnus restent explicitement signalés. SQLite integrity_check : ok. Les captures précédentes restent dans l'historique et ne doivent pas être additionnées.
