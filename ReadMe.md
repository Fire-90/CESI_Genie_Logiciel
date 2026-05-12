# Documentation Technique - EasySave Version 3.0

EasySave 3.0 est une solution logicielle avancée de sauvegarde de données développée en C# sous .NET 8.0 (WPF). Cette version franchit une étape majeure en introduisant l'exécution parallèle des travaux de sauvegarde (Multithreading), une gestion fine des priorités de transfert et un contrôle interactif en temps réel des processus. L'architecture MVVM est ici renforcée pour supporter des opérations asynchrones complexes et thread-safe.

## Évolutions Majeures

* **Exécution Parallèle (Multithreading) :** Capacité de lancer simultanément plusieurs travaux de sauvegarde. Chaque travail s'exécute dans un thread dédié (via la Task Parallel Library), optimisant ainsi l'utilisation des ressources CPU et réduisant le temps global de traitement.
* **Système de Barrière pour Fichiers Prioritaires :** Implémentation d'un mécanisme de synchronisation globale. Les fichiers possédant des extensions prioritaires (configurables) sont traités en premier sur l'ensemble des travaux actifs. Les fichiers non prioritaires sont mis en attente jusqu'à ce que tous les fichiers prioritaires de tous les travaux en cours soient transférés.
* **Contrôle de Flux Interactif :** L'utilisateur dispose désormais de commandes individuelles pour mettre en pause, reprendre ou arrêter définitivement un travail de sauvegarde spécifique en cours d'exécution.
* **Surveillance en Temps Réel :** Intégration de barres de progression dynamiques dans l'interface graphique, offrant un retour visuel immédiat sur l'état d'avancement de chaque thread de sauvegarde.
* **Optimisation de l'Interface Vectorielle :** Remplacement des polices de caractères pour les icônes par des tracés vectoriels (Paths XAML), garantissant une netteté parfaite et une compatibilité totale quel que soit l'environnement Windows.

## Fonctionnalités Principales

* **Gestion Multi-Travaux :** Administration illimitée des travaux avec sélection multiple pour exécution groupée.
* **Algorithmes de Sauvegarde :**
    * *Complète :* Réplication intégrale des structures sources.
    * *Différentielle :* Analyse des horodatages pour ne copier que les fichiers modifiés.
* **Sécurité et Intégrité :**
    * Chiffrement via le module `CryptoSoft` pour les extensions sensibles.
    * Détection des logiciels métiers bloquants avec interruption automatique du flux de données.
* **Journalisation et État :**
    * `state.json` : Suivi thread-safe de la progression et des fichiers restants.
    * `logs/` : Génération quotidienne de journaux au format JSON ou XML, incluant les métadonnées de transfert et les temps de réponse.

## Architecture Technique

* **`EasySave.ViewModels` :** Utilisation de `SynchronizationContext` pour assurer la mise à jour sécurisée de l'interface graphique depuis les threads d'arrière-plan.
* **`EasySave.Services.BackupEngine` :** * Utilisation de `ConcurrentDictionary` pour le suivi des jetons d'annulation et des événements de pause.
    * Implémentation d'opérations atomiques via `Interlocked` pour le compteur global des fichiers prioritaires.
    * Verrouillage (`lock`) des ressources critiques pour garantir l'intégrité du fichier d'état lors d'accès concurrents.
* **`EasyLog` :** Module de journalisation optimisé pour l'écriture asynchrone.

## Prérequis Système

* Environnement Windows (WPF .NET 8.0).
* Processeur multi-cœurs recommandé pour bénéficier pleinement des performances du multithreading.
* L'exécutable `CryptoSoft.exe` doit être présent dans le répertoire racine pour les fonctions de chiffrement.

## Installation et Compilation

```bash
git clone [URL_DU_DEPOT]
cd EasySave
dotnet build
