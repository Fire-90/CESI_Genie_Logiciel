# EasySave - Version 1.0

EasySave est une application console logicielle de sauvegarde de données, développée en C# sur le framework .NET 8.0. Ce projet a été conçu en respectant une architecture Modèle-Vue-Contrôleur (MVC) stricte, garantissant une séparation claire entre la logique métier, la gestion des données et l'interface utilisateur.

## Fonctionnalités Principales

* **Gestion de 5 travaux de sauvegarde :** Configuration, mémorisation et exécution de 5 emplacements (slots) distincts.
* **Types de sauvegarde :**
  * *Complète :* Copie intégrale du répertoire source.
  * *Différentielle :* Copie uniquement des fichiers modifiés ou nouveaux par rapport à la cible.
* **Support Multilingue :** Sélection de la langue (Anglais / Français) au démarrage de l'application.
* **Modes d'exécution :**
  * *Mode Interactif :* Menu textuel permettant de configurer des travaux et de sélectionner les sauvegardes à lancer.
  * *Ligne de commande (CLI) :* Exécution directe via des arguments (ex: `1`, `1-3`, `1;5`).
* **Suivi et Journalisation (Dossier `/data`) :**
  * `state.json` : Fichier mis à jour en temps réel (fichiers restants, progression en %).
  * `config.json` : Sauvegarde persistante de la configuration des travaux.
  * `logs/YYYY-MM-DD.json` : Journalisation quotidienne des opérations de transfert et des temps d'exécution.

## Architecture du Projet (MVC)

Le projet est organisé selon les espaces de noms suivants pour préparer la transition vers une future interface graphique (MVVM) :

* **`EasySave.Models` :** Contient les structures de données passives (`BackupJob`, `BackupType`, `LogEntry`) et la classe de journalisation (`DailyLogger`).
* **`EasySave.Views` :** Gère exclusivement les entrées/sorties avec l'utilisateur via la console (`ConsoleView`).
* **`EasySave.ViewModels` (Contrôleurs) :** Pilote la logique de l'application.
  * `BackupEngine` : Moteur gérant les algorithmes de copie complète et différentielle.
  * `ConfigManager` : Gère la persistance de la configuration.
  * `StateTracker` : Maintient et actualise l'état en temps réel.
* **`EasySave.Core` :** Outils transverses, incluant le gestionnaire de traduction (`LanguageManager`).

## Prérequis

* SDK .NET 8.0 ou supérieur.
* Visual Studio 2022 (recommandé) ou Visual Studio Code.

## Installation et Compilation

1. Cloner le dépôt Git :
   ```bash
   git clone [URL_DU_DEPOT]
   ```
2. Accéder au répertoire du projet :
   ```bash
   cd EasySave
   ```
3. Compiler le projet :
   ```bash
   dotnet build
   ```

## Utilisation

Le programme peut être lancé de deux manières depuis le terminal.

### 1. Mode Interactif (Menu)
Lancez l'exécutable sans argument. L'application vous demandera de choisir la langue, puis affichera le menu de configuration et d'exécution. Si un emplacement sélectionné est vide, l'application proposera de le configurer interactivement.
```bash
dotnet run
```

### 2. Mode Ligne de Commande (CLI)
Passez les identifiants des travaux à exécuter en argument. L'application sélectionnera la langue par défaut (Anglais) ou demandera la langue, puis exécutera directement les travaux sans afficher le menu.

* **Exécuter un seul travail :**
  ```bash
  dotnet run "2"
  ```
* **Exécuter une plage de travaux consécutifs :**
  ```bash
  dotnet run "1-3"
  ```
* **Exécuter des travaux spécifiques :**
  ```bash
  dotnet run "1;4;5"
  ```

## Emplacement des Fichiers de Données

Afin de respecter les contraintes de déploiement serveur, aucun chemin n'est codé en dur (comme `C:\temp\`). Tous les fichiers générés par l'application sont stockés dans un sous-dossier `data` situé au même niveau que le fichier exécutable (`.exe`).

```text
[Dossier_Executable]/
├── EasySave.exe
└── data/
    ├── config.json
    ├── state.json
    └── logs/
        ├── 2026-04-21.json
        └── 2026-04-22.json
```