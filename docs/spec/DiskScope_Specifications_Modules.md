> Extracted from DiskScope_Specifications_Modules.docx (same folder, the .docx is the source of truth). Received 2026-10-19.

# DiskScope

Spécifications des modules de monitoring, diagnostic et maintenance Windows

## Base technique : C# · .NET 10 · WPF · MVVM

Ce document regroupe les spécifications proposées pour les modules à développer après le module Stockage, déjà confié à l’agent de développement. Il définit une architecture commune, les fonctionnalités, les sources de données, les risques et une feuille de route progressive.

# 1. Vision du produit

DiskScope doit devenir un centre de contrôle Windows permettant de comprendre ce qui consomme les ressources, d'identifier les ralentissements et d'effectuer une maintenance prudente et réversible. L'objectif n'est pas d'afficher artificiellement une utilisation minimale, mais de fournir des mesures fiables et des diagnostics vérifiables.

- Application Windows native en lecture seule pour les fonctions de mesure.

- Actions de maintenance séparées, explicitement confirmées et réversibles lorsque c'est possible.

- Historique local, alertes explicables et corrélation entre ressources.

- Aucune suppression automatique, désactivation de service ou modification système sans confirmation explicite.

- Le module Stockage est déjà en développement ; les autres modules doivent s'intégrer sans le coupler fortement au cœur.

# 2. Périmètre et priorités

| Module | Priorité | Complexité indicative | Objectif principal |
|---|---|---|---|
| Monitoring CPU/RAM | Très haute | Moyenne | Mesures globales et par processus, tendances |
| Process Explorer | Très haute | Moyenne | Arbre de processus et ressources consommées |
| Historique & alertes | Haute | Moyenne | Retrouver les causes possibles d'un ralentissement |
| GPU Monitor | Haute | Élevée | Moteurs GPU, VRAM et capteurs disponibles |
| Dashboard | Haute | Faible à moyenne | Vue synthétique en temps réel |
| Santé matérielle | Moyenne | Élevée | Températures, fréquences, ventilateurs si exposés |
| Démarrage & services | Moyenne | Moyenne | Inventaire et actions réversibles |
| Rapports & export | Moyenne | Faible | Rapports lisibles et exportables |
| Diagnostic ETW | Basse au départ | Très élevée | Analyse approfondie des blocages et latences |

# 3. Architecture commune

Conserver une architecture en couches, avec des collecteurs indépendants et des contrats stables.

```
DiskScope.App                 WPF / MVVM / navigation / vues
DiskScope.Core                modèles, contrats, règles de diagnostic
DiskScope.Monitoring          orchestration des collecteurs et calendrier
DiskScope.Monitoring.CpuRam   métriques CPU et mémoire
DiskScope.Monitoring.Gpu      compteurs GPU et adaptateurs matériels
DiskScope.Processes           inventaire et arbre des processus
DiskScope.History             persistance locale et rétention
DiskScope.Diagnostics         alertes, corrélations et règles explicables
DiskScope.Hardware            capteurs et santé matérielle
DiskScope.Maintenance         démarrage, tâches et services
DiskScope.Reports             exports et rapports
DiskScope.Tests               tests unitaires et d'intégration
DiskScope.Benchmarks          mesures reproductibles
```

Les noms sont proposés : si la solution existante a déjà une convention de nommage ou un projet Core, la réutiliser plutôt que créer des doublons.

## Contrats communs

- IMetricCollector : collecte des métriques d'une catégorie, avec intervalle configurable, annulation et état de disponibilité.

- IMetricStore : persistance et interrogation de mesures horodatées.

- IAlertEngine : évaluation de règles, durée de dépassement, sévérité, preuves et acquittement.

- IDiagnosticRule : règle déterministe qui fournit un constat, les données observées, le niveau de confiance et les limites.

- IHardwareTelemetryProvider : adaptateur optionnel pour les capteurs spécifiques au matériel.

- Les collecteurs publient par lots ; ils ne manipulent pas directement les contrôles WPF.

## Principes non négociables

- Pas d'affirmation de réussite sans exécution effective des tests ou benchmarks.

- Pas de dépendance forte entre l'interface, les collecteurs et le stockage.

- Gérer les métriques indisponibles : ne pas afficher zéro lorsqu'une valeur est inconnue.

- Chaque métrique doit préciser son unité, sa source, son horodatage et, si nécessaire, son statut de qualité.

- La collecte doit avoir un coût mesuré et une fréquence configurable.

# 4. Module Monitoring CPU & RAM

## Objectif

Mesurer la charge et l'évolution de la mémoire, avec un historique suffisant pour repérer une pression mémoire ou une consommation anormale.

## CPU

- Utilisation globale et par processeur logique, calculée à partir de différences de compteurs dans le temps.

- Temps CPU user/kernel lorsque disponible.

- Consommation CPU par processus, à partir des deltas de temps processeur et du temps écoulé.

- Fréquence CPU uniquement si une source fiable l'expose ; distinguer fréquence nominale, rapportée et effective.

## RAM

- Mémoire physique totale, disponible et utilisée.

- Mémoire engagée (commit) et limite d'engagement.

- Cache, pool paginé et non paginé lorsque les compteurs correspondants sont disponibles.

- Mémoire par processus : working set, working set privé et mémoire privée/engagée selon les API utilisées.

- Tendance temporelle d'un processus ; ne qualifier une fuite que comme hypothèse tant qu'elle n'est pas confirmée.

## Sources techniques

| Besoin | API / source | Notes |
|---|---|---|
| CPU global | GetSystemTimes | Calcul par différence entre deux échantillons |
| CPU processus | GetProcessTimes | Normaliser par le nombre de processeurs logiques si un pourcentage système est affiché |
| RAM globale | GlobalMemoryStatusEx | État global physique et mémoire virtuelle |
| RAM processus | GetProcessMemoryInfo | Working set ; compléter avec les compteurs adaptés pour la mémoire privée |
| Compteurs additionnels | PDH / Performance Counters | Utiliser avec une fréquence raisonnable et tester les noms localisés des compteurs |

## Rafraîchissement et rétention

- Point de départ : collecte légère toutes les 1 seconde, configurable.

- Rétention indicative : 1 seconde pendant 10 minutes, 5 secondes pendant 1 heure, 1 minute pendant 24 heures.

- Les règles de rétention doivent être configurables ; agréger les anciens points plutôt que conserver tous les échantillons bruts indéfiniment.

## Alertes initiales

- CPU supérieur à 90 % pendant 30 secondes, avec seuil configurable et délai anti-rebond.

- Mémoire disponible très faible ou engagement proche de sa limite ; ne pas se baser uniquement sur le pourcentage de RAM occupée.

- Croissance soutenue de la mémoire d'un processus sur une fenêtre temporelle, présentée comme suspicion à vérifier.

## Critères d'acceptation

- Les compteurs sont calculés avec les unités documentées et des horodatages monotones/cohérents.

- Un processus terminé ou inaccessible ne fait pas planter la collecte.

- La charge ajoutée par le monitoring est mesurée et reste faible sur la machine de référence.

# 5. Module GPU Monitor

Fournir une vue fidèle des moteurs GPU et de la mémoire vidéo, en signalant explicitement les mesures indisponibles selon le pilote.

## Métriques visées

- Utilisation par moteur : 3D, Compute, Copy, Encode, Decode et autres moteurs exposés.

- Mémoire GPU dédiée et partagée ; distinguer budget, utilisation et capacité physique lorsque les API fournissent ces champs.

- Température, fréquence, puissance et vitesse de ventilateur uniquement si une source fiable les expose.

- Données par adaptateur afin de gérer les systèmes multi-GPU.

## Sources et conception

- Explorer les compteurs GPU Windows et les interfaces DXGI/D3DKMT appropriées.

- IDXGIAdapter3::QueryVideoMemoryInfo peut fournir des informations de mémoire vidéo liées au processus et au segment ; ne pas le présenter comme une mesure universelle de toute la VRAM.

- Créer une abstraction de fournisseur pour ajouter, si nécessaire, des intégrations constructeur distinctes.

- Ne pas dépendre d'un seul constructeur et ne pas supposer que températures ou puissances sont toujours disponibles.

## Risques

- Les compteurs disponibles varient selon Windows, pilote et matériel.

- Une utilisation GPU globale peut masquer un moteur saturé ; montrer les moteurs séparément.

- La mémoire partagée n'est pas équivalente à la VRAM dédiée.

## Critères d'acceptation

- Un adaptateur indisponible n'empêche pas le reste du monitoring de fonctionner.

- Les métriques ont une source, une unité et un statut d'indisponibilité explicite.

- Les données sont comparées au Gestionnaire des tâches ou à un outil de référence, en documentant les différences de définition.

# 6. Module Process Explorer

Fournir une vue hiérarchique des processus, leurs ressources et les informations utiles au diagnostic.

## Fonctionnalités

- Arbre parent/enfant construit avec PID, PPID et heure de création ; gérer la réutilisation des PID.

- CPU, working set, mémoire privée, E/S lorsque disponibles, et rattachement aux métriques GPU si la source permet une attribution fiable.

- Nom d'image, chemin exécutable, ligne de commande selon les permissions, utilisateur et heure de démarrage.

- Nombre de threads et handles si la collecte est fiable et si son coût est acceptable.

- Signature numérique et éditeur comme informations complémentaires, avec chargement à la demande.

- Recherche, filtres, tri et affichage des processus terminés dans l'historique si activé.

## Sécurité et exactitude

- Ne pas tuer de processus ni modifier leur priorité dans la première version.

- Ne pas supposer que les informations de ligne de commande ou de modules sont accessibles pour tous les processus.

- Identifier un processus par PID et heure de création, pas uniquement par PID.

- Ne pas promettre une attribution GPU par processus si la source ne la fournit pas.

## Critères d'acceptation

- Les processus qui apparaissent ou disparaissent pendant l'actualisation sont gérés sans erreur fatale.

- Les colonnes indiquent clairement leurs unités et leur méthode de calcul.

- Les opérations coûteuses (signature, modules, détails) sont différées ou déclenchées à la demande.

# 7. Module Dashboard

Présenter l'état du PC en un coup d'œil et permettre d'accéder aux modules détaillés.

- Cartes CPU, RAM, GPU/VRAM, stockage, réseau et capteurs disponibles.

- Valeur actuelle, tendance courte, maximum récent et état de disponibilité.

- Chaque carte ouvre le module détaillé correspondant.

- Métriques légères : intervalle indicatif de 1 seconde ; capteurs plus coûteux : 2 à 5 secondes selon le fournisseur.

- Éviter de recopier les données : le dashboard consomme le modèle commun et les dernières mesures publiées.

- Afficher les lacunes de données ; ne pas inventer de température ou de métrique absente.

# 8. Module Historique & Alertes

## Objectif

Conserver localement des séries temporelles et permettre de retrouver ce qui s'est produit avant ou pendant un ralentissement.

## Fonctionnalités

- Historique consultable par plage temporelle, ressource et processus.

- Règles configurables avec seuil, durée minimale, période de récupération et délai anti-rebond.

- Événement d'alerte avec horodatage, mesures sources, sévérité, explication et statut d'acquittement.

- Agrégation des anciennes mesures et rétention configurable.

- SQLite est une option adaptée au stockage local ; définir une stratégie d'écriture groupée et de migration du schéma.

## Exigences

- Distinguer une alerte factuelle (seuil mesuré) d'une hypothèse de diagnostic.

- Éviter les alertes répétées pour le même épisode.

- L'utilisateur peut désactiver une règle ou modifier son seuil.

- L'application continue de fonctionner si la base d'historique est temporairement indisponible ; signaler la perte éventuelle de mesures.

# 9. Module Santé matérielle

Regrouper les capteurs disponibles sans prétendre que tous les matériels exposent les mêmes informations.

- Températures CPU/GPU/SSD, fréquences, puissance, tensions et ventilateurs uniquement lorsqu'ils sont fournis par une source identifiable.

- WMI peut compléter certaines données ; privilégier une interface fournisseur pour les capteurs spécifiques.

- Évaluer l'utilisation d'une bibliothèque compatible, par exemple LibreHardwareMonitor, après vérification de sa licence, de sa maintenance et de sa compatibilité.

- Distinguer seuil constructeur, seuil configurable et simple avertissement générique.

- Ne pas déclencher d'action matérielle automatique ; ne pas modifier courbes de ventilateurs ou tensions dans le périmètre initial.

## Critères d'acceptation

- Chaque capteur indique son nom, unité, source et état de validité.

- Les valeurs impossibles ou figées sont signalées plutôt que présentées comme fiables.

- Les seuils sont adaptés au composant lorsqu'ils sont documentés ; sinon l'interface précise qu'il s'agit d'un seuil générique.

# 10. Module Démarrage & Services

Aider à examiner les éléments lancés automatiquement, sans désactiver des composants au hasard.

## Sources à inventorier

- Applications de démarrage et clés Run/RunOnce du registre.

- Dossiers de démarrage utilisateur et système.

- Tâches planifiées.

- Services Windows : type de démarrage, état, compte et dépendances.

## Fonctionnalités et garde-fous

- Afficher l'éditeur, le chemin, la signature lorsqu'elle est disponible et la source de l'entrée.

- Ne pas afficher un score d'impact inventé ; utiliser des mesures observées ou préciser que l'impact est inconnu.

- Toute modification demande une confirmation et doit être réversible lorsque possible.

- Avant de modifier un service ou une tâche, montrer les dépendances et le risque connu.

- La première version peut être en lecture seule, avec export d'inventaire.

# 11. Module Diagnostic ETW

Module expert à développer après stabilisation des collecteurs et de l'historique.

- Lancer des sessions de trace explicites et bornées dans le temps.

- Explorer les événements CPU, E/S disque, ordonnancement, DPC/ISR et événements graphiques selon les fournisseurs activés.

- Fournir une chronologie corrélée avec les métriques de base.

- Gérer les permissions, le coût de collecte, la taille des traces et leur suppression/archivage.

- Éviter d'implémenter prématurément un clone complet de Windows Performance Analyzer ; commencer par un scénario de diagnostic précis.

- Ne pas déclarer une cause racine si les événements ne suffisent qu'à suggérer une corrélation.

# 12. Module Rapports & Export

- Export CSV des mesures, processus et inventaires.

- Export JSON structuré pour les diagnostics et l'automatisation.

- Rapport HTML local avec résumé, plage temporelle, graphiques, alertes et limites de collecte.

- PDF en phase ultérieure si le besoin est confirmé.

- Les rapports doivent indiquer la date, la version de l'application, les sources de données et les métriques indisponibles.

- Ne pas inclure par défaut les lignes de commande complètes ou autres données potentiellement sensibles dans un rapport partagé.

# 13. Moteur de diagnostic et corrélation

Le moteur de diagnostic transforme les métriques en constats explicables. Chaque constat doit conserver les preuves et les limites.

| Niveau | Signification | Exemple |
|---|---|---|
| Information | État observé sans problème établi | Un processus utilise actuellement 35 % du CPU |
| À surveiller | Tendance inhabituelle ou seuil soutenu | Mémoire privée croissante pendant plusieurs minutes |
| Alerte | Condition mesurée et persistante | Mémoire disponible sous un seuil configuré |
| Hypothèse | Explication possible, à confirmer | Le ralentissement coïncide avec une activité disque élevée |

- Chaque règle doit documenter la formule, la fenêtre temporelle, les seuils et les faux positifs connus.

- Une corrélation temporelle n'est pas une preuve de causalité.

- Afficher les données qui ont déclenché le diagnostic et proposer des vérifications concrètes.

- Toute action corrective doit rester distincte du diagnostic et nécessiter une confirmation.

# 14. Benchmark et qualité

## Scénarios de test

- PC au repos pendant 10 minutes.

- Charge CPU soutenue et charge sur un seul cœur.

- Allocation mémoire progressive et processus terminés pendant la collecte.

- GPU utilisé en 3D, compute et mémoire vidéo, lorsque le matériel de test le permet.

- Forte activité disque avec débit faible ou latence élevée.

- Nombre élevé de processus, accès refusés et compteurs indisponibles.

## Mesures à publier

- Consommation CPU et mémoire propre de DiskScope.

- Latence et durée de chaque collecte.

- Taux d'échantillons manquants et erreurs par fournisseur.

- Coût de l'historique en espace disque et effet de la rétention.

- Comparaison avec les outils Windows de référence, en documentant les différences de définition.

Les objectifs chiffrés de performance doivent être établis après mesure d'une version de base, et non annoncés comme acquis.

# 15. Roadmap recommandée

- Étape A : inspecter la solution existante et son architecture ; ne pas créer de projets doublons.

- Étape B : définir les contrats partagés de métriques, états, erreurs et annulation.

- Étape C : développer CPU/RAM et Process Explorer avec tests.

- Étape D : ajouter le dashboard et l'historique local.

- Étape E : développer GPU avec une matrice de compatibilité matérielle.

- Étape F : ajouter capteurs matériels et inventaire de démarrage/services.

- Étape G : développer le moteur de corrélation puis un premier scénario ETW.

- Étape H : finaliser les exports, la documentation et les tests de régression.

# 16. Instructions communes à Claude Code

À utiliser comme consignes persistantes dans le dépôt, ou à rappeler au début de chaque module. Adapter les noms de projets à la solution existante avant toute modification.

```
Agis comme un ingénieur senior C#/.NET/WPF spécialisé dans les API Windows et le diagnostic de performances.
Avant de coder :
1. Inspecte la solution et lis les conventions existantes.
2. Identifie les projets déjà créés, en particulier le module Stockage en cours.
3. Propose un plan limité au module demandé.
4. Vérifie les API dans la documentation Microsoft officielle.
Pendant le développement :
- Respecte l'architecture existante et évite les duplications.
- Sépare les collecteurs, le modèle de données et l'interface MVVM.
- Gère annulation, erreurs, permissions et métriques indisponibles.
- Ne présente pas une valeur inconnue comme zéro.
- Ne lance aucune action destructive ou modification système sans demande explicite.
- Ajoute des tests pour les calculs et les cas limites.
- Compile et exécute les tests après chaque étape.
- N'affirme pas qu'un test ou benchmark a réussi s'il n'a pas été exécuté.
- Mesure les performances au lieu de les supposer.
- Termine chaque étape par un résumé des changements, résultats de test et limites restantes.
Commence par inspecter le dépôt et propose les prochaines étapes sans réécrire les modules déjà en cours.
```

# 17. Références techniques de départ

- Microsoft Learn — GetSystemTimes — https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes

- Microsoft Learn — GlobalMemoryStatusEx — https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex

- Microsoft Learn — GetProcessMemoryInfo — https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getprocessmemoryinfo

- Microsoft Learn — Performance Counters — https://learn.microsoft.com/en-us/windows/win32/perfctrs/about-performance-counters

- Microsoft Learn — DXGI_QUERY_VIDEO_MEMORY_INFO — https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/ns-dxgi1_4-dxgi_query_video_memory_info

- Microsoft Learn — WPF — https://learn.microsoft.com/en-us/dotnet/desktop/wpf/

- Microsoft Learn — Event Tracing for Windows — https://learn.microsoft.com/en-us/windows/win32/etw/event-tracing-portal

Note : Ce document est une spécification initiale de conception, pas une certification des API ou des performances. Les API précises, permissions, limites de compatibilité et dépendances doivent être vérifiées lors de l'implémentation.

