# Notice relative aux ressources graphiques de DOFUS

**Projet :** BestCrush / DofusSharp  
**Statut :** développement et tests — autorisation d'utilisation et de redistribution des visuels en attente de confirmation  
**Dernière mise à jour :** 24 octobre 2026

## 1. Titularité des droits

DOFUS, ses univers, personnages, illustrations, portraits de classes, icônes de métiers, graphismes et autres ressources issues du jeu sont des créations protégées dont les droits appartiennent à **Ankama** et/ou aux titulaires de droits concernés.

Ces éléments ne sont **pas** couverts par la licence du code source de BestCrush ou de DofusSharp. La disponibilité d'une image sur un site tiers, dans une archive communautaire ou dans les fichiers du jeu ne vaut pas autorisation de la reproduire ou de la redistribuer.

## 2. Utilisation pendant le développement

BestCrush utilise actuellement, à titre provisoire pour ses essais de développement, des ressources graphiques provenant de DOFUS, notamment des **icônes de métiers** et des **portraits de classes**.

Leur récupération est réalisée séparément, sur le poste de développement, par `scripts/install_dofus_artwork.ps1`. Les fichiers sont chargés localement par l'application. Ils sont exclus du suivi Git dans `.gitignore`.

**À la date de cette notice, aucune autorisation d'Ankama concernant l'utilisation ou la redistribution de ces images n'est confirmée dans les documents du projet.** Une demande de clarification/autorisation est en cours auprès d'Ankama.

L'usage limité au développement privé ne constitue pas, à lui seul, une autorisation accordée par le titulaire des droits.

## 3. Distribution et publication

Dans l'attente d'une réponse écrite précisant le périmètre autorisé :

- Les images officielles ne doivent pas être ajoutées au dépôt public, à un paquet de release, à un installeur ou à une distribution destinée à des tiers.
- Les contributeurs qui construisent une version publique doivent vérifier qu'aucun PNG officiel téléchargé localement n'est accidentellement embarqué dans le résultat publié.
- Le logiciel conserve des illustrations provisoires pour les éléments non installés ; la future distribution pourra également employer des visuels originaux ou dûment licenciés.

Toute utilisation des ressources Ankama en distribution publique sera réexaminée à réception d'un accord applicable, notamment en fonction de son objet, de sa durée, de ses éventuelles conditions d'attribution et de la possibilité de les embarquer dans l'installateur.

**Protection technique :** tant qu'aucune autorisation de redistribution des visuels n'est documentée, le projet `BestCrush.csproj` interrompt une compilation `Release` si des PNG d'Ankama sont présents sous `BestCrush/wwwroot/img/dofus/`. Le workflow de publication GitHub Actions effectue également un contrôle des fichiers de l'archive compilée. Les compilations `Debug` restent possibles avec les images installées localement. Cette protection doit être réévaluée à réception de l'accord écrit.

## 4. Indépendance du projet

BestCrush est un projet logiciel compagnon indépendant. **Cette notice ne prétend ni à un partenariat, ni à un parrainage, ni à une approbation d'Ankama concernant les ressources graphiques.**

Le porteur du projet indique avoir reçu un retour distinct d'Ankama au sujet du travail de capture réseau. Cet échange n'est **pas** assimilé ici à une autorisation d'utiliser ou redistribuer les images, et sa portée exacte n'est pas attestée par la présente notice.

## 5. Références techniques et contact

Pour connaître la provenance et les empreintes des archives utilisées pendant le développement, consulter `docs/modular-workspace-p3d3-local-artwork.md`.

Pour toute demande concernant les droits sur les ressources de DOFUS, s'adresser à Ankama via ses canaux officiels. Toute demande concernant les fichiers distribués par BestCrush doit être examinée avant publication.

> **Mise à jour à prévoir :** si Ankama délivre une autorisation écrite, ajouter la date, le périmètre exact, les conditions et une référence vérifiable à cet accord, sans en extrapoler la portée.
