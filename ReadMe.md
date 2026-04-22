# Documentation EasySave - Version 1.0

EasySave est une application console logicielle de sauvegarde de données, développée en C# sur le framework .NET 8.0. Ce projet a été conçu en respectant une architecture Modèle-Vue-Contrôleur (MVC), garantissant une séparation claire entre la logique métier, la gestion des données et l'interface utilisateur.

## Fonctionnalités Principales

* **Gestion des travaux de sauvegarde :** Configuration, mémorisation et exécution de 5 emplacements (slots) distincts.
* **Types de sauvegarde :**
  * *Complète :* Copie intégrale du répertoire source vers le répertoire cible.
  * *Différentielle :* Copie exclusive des fichiers modifiés ou nouveaux par rapport à la cible.
* **Support Multilingue :** Sélection de la langue (Anglais / Français) au démarrage de l'application.
* **Modes d'exécution :**
  * *Mode Interactif :* Menu textuel permettant de configurer les travaux et de sélectionner les sauvegardes à lancer.
  * *Ligne de commande (CLI) :* Exécution directe via des arguments (ex. : `1`, `1-3`, `1;5`).
* **Suivi et Journalisation :**
  * `state.json` : Fichier mis à jour en temps réel contenant l'état des sauvegardes (fichiers restants, progression en %).
  * `config.json` : Sauvegarde persistante de la configuration des travaux.
  * `logs/YYYY-MM-DD.json` : Journalisation quotidienne des opérations de transfert et des temps d'exécution.

## Architecture du Projet

Le code source est organisé selon les espaces de noms suivants :

* **`EasySave.Models` :** Contient les structures de données passives (`BackupJob`, `BackupType`).
* **`EasySave.Views` :** Gère exclusivement les interactions et l'affichage dans la console (`ConsoleView`).
* **`EasySave.Controller` :** Pilote la logique applicative.
  * `BackupEngine` : Moteur gérant les algorithmes de copie complète et différentielle.
  * `ConfigManager` : Gère le chargement et la persistance de la configuration.
  * `StateTracker` : Maintient et actualise l'état des sauvegardes en temps réel.
* **`EasySave.Core` :** Regroupe les outils transverses, incluant le gestionnaire de traduction (`LanguageManager`).
* **`EasyLog` :** Module dédié à la journalisation quotidienne (`DailyLogger`, `LogEntry`).

## Prérequis

* SDK .NET 8.0 ou supérieur.
* Environnement de développement compatible (Visual Studio 2022 ou Visual Studio Code recommandés).

## Installation et Compilation

Pour récupérer et compiler le projet, il est recommandé d'exécuter les commandes suivantes dans un terminal :

```bash
git clone [URL_DU_DEPOT]
cd EasySave
dotnet build
```

## Utilisation

Il est possible de lancer le programme de deux manières.

### 1. Mode Interactif (Menu)
Une exécution sans argument permet de lancer l'interface utilisateur. Le programme demandera la sélection de la langue, puis affichera le menu principal. Si un emplacement de sauvegarde sélectionné est vide, une configuration interactive sera proposée.

```bash
.\EasySave.exe
```

### 2. Mode Ligne de Commande (CLI)
Il est possible de fournir les identifiants des travaux à exécuter directement en tant qu'arguments. L'application exécutera alors les travaux spécifiés de manière séquentielle.

* **Exécuter un seul travail :**
  ```bash
  .\EasySave.exe "2"
  ```
* **Exécuter une plage de travaux consécutifs :**
  ```bash
  .\EasySave.exe "1-3"
  ```
* **Exécuter des travaux spécifiques :**
  ```bash
  .\EasySave.exe "1;4;5"
  ```

## Emplacement des Fichiers de Données

Pour éviter l'utilisation de chemins codés en dur, tous les fichiers générés et utilisés par l'application sont stockés dans un sous-dossier `data` créé automatiquement au même niveau que le fichier exécutable.

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