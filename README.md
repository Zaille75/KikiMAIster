<p align="center"><img src="docs/images/icon.png" width="160" alt="KikiMAIster"></p>

# KikiMAIster

**All-in-one stats for Slay the Spire 2, made for co-op.**
Damage, protection, debuffs, help between players, missed opportunities, trophies and records.
Read-only: it never changes the game. Works in solo and in multiplayer, **even when your friends don't have it**.

*[Version française plus bas](#français)*

## What it shows

### The panel (during a run)
A compact table, one line per player, with three tabs: **last turn**, **combat** (◂ ▸ browses the previous
fights) and **run**. Click a section title to unfold it, click a player's line to see the details under it.

| Section | What you see |
|---|---|
| **Damage** | Total and share, by source (cards, poison & other damage over time, powers, pet, orbs, relics, potions), extra damage your debuffs gave, damage your debuffs avoided, HP lost (from enemies, yourself, allies), Max HP gained / lost during the run |
| **Protection** | Damage actually stopped: block, pet, relics/powers; block that stopped nothing; the details by block source ("Defend: 18 stopped · 5 useless") |
| **Debuffs** | Each debuff applied, as times applied / total stacks |
| **Missed opportunities** | Missed lethal turns, wasted block, weaker choices (smaller attack, wrong order…), unused energy, avoidable HP, relic triggers missed by one card, potions left at death — each with a short explanation |
| **Co-op** | Block given, HP saved for allies, damage taken for them, damage enabled for allies, energy and cards given, friendly fire, deaths, rock-paper-scissors for relics |
| **Campfires** | Who chose what at each campfire |
| **Cards & potions** | Drawn, played, exhausted, discarded, potions used |

Every number follows a written rule: block is consumed oldest first, help only counts when it really saved HP or
dealt damage, a card kept on purpose (Retain, Stars, Exhaust…) is never a mistake. The full rules are in
[docs/specs/01-metrics.md](docs/specs/01-metrics.md).

### Other windows
- **Combat summary:** a chart, round by round, of damage, HP lost, debuffs or HP saved, for every fight of the run.
- **Trophies:** at the end of the run (or any time), the best player in each category, with all-time records,
  personal bests and records per character. Solo and co-op records are kept apart.
- **Group compositions** (F8, also in the lobby): how often each group of characters was played and won, per
  ascension level, for all your runs or only this group of players; a table of who played what; a composition
  never played or never won to try next. Built from the game's own run history.

### Settings (⚙)
Language (English / Français), which sections and columns to show, hide columns at 0, put any section in its own
window, show the combat summary automatically.

## Keys (change them in the settings, or from the game's Mods screen)
- **F7**: show / hide the panel
- **F8**: group compositions
- **F9**: report a bug, a wrong stat or an idea (also in the settings). The mod makes a zip with a screenshot, the game
  state and its log, with player names and Steam ids removed, then opens a GitHub issue page: drop the zip on it.

## Install
**Steam Workshop** (easiest, updates are automatic): subscribe to
[KikiMAIster](https://steamcommunity.com/sharedfiles/filedetails/?id=3832591950), then start the game. Since 1.1.0 it
works on both game branches (beta v0.113.0 and default v0.111.0; 1.2.0 is a first quick update for the new beta, more will follow soon).

Manual install, game closed:
1. Download the release zip (`KikiMAIster.dll` and `KikiMAIster.json`).
2. Put both files in `<Steam library>\steamapps\common\Slay the Spire 2\mods\KikiMAIster\`.
3. Start the game and enable the mod in the mods menu if needed.

Only one player needs the mod: every player's game simulates the whole fight, so you see everyone's stats.

The mod keeps its own files in the game's user folder (`%APPDATA%\SlayTheSpire2\KikiMAIster` on Windows), so mod
updates never erase them: settings, records, compositions, rock-paper-scissors results, run files (`runs/`, to keep the
stats across Save & Quit) and `KikiMAIster.log`.

## Compatibility
Made for Slay the Spire 2 (early access): one build for the **default** and **beta** branches. Game updates can break some hooks; the mod catches
its own errors so it never crashes the game.

## Building from source
.NET 9 SDK, C# with Harmony. Copy the game's `sts2.dll`, `GodotSharp.dll`, `0Harmony.dll` and `release_info.json`
into `lib/beta/`, and the default branch's into `lib/default/` (game files are not in this repository), then run
`./build.sh`: it builds against the beta, checks on each branch that every patch matches a game method and every game
member the mod uses exists, runs the unit tests and puts the files to install in `dist/KikiMAIster/`.

## License
[MIT](LICENSE) © Zaille75. Slay the Spire 2 belongs to Mega Crit; no game file is included.

---

## Français

**Toutes les stats de Slay the Spire 2, pensées pour la coop.**
Dégâts, protection, affaiblissements, entraide, occasions manquées, trophées et records.
Le mod ne change rien au jeu. Il marche en solo et en multi, **même si tes amis ne l'ont pas**.

### Ce qu'il affiche

#### Le panneau (pendant une partie)
Un tableau compact, une ligne par joueur, avec trois onglets : **tour précédent**, **combat** (◂ ▸ pour revoir les
combats précédents) et **partie**. Clique sur le titre d'une section pour la déplier, sur la ligne d'un joueur pour
voir le détail en dessous.

| Section | Ce que tu vois |
|---|---|
| **Dégâts** | Total et part, par source (cartes, poison et autres dégâts sur la durée, pouvoirs, familier, orbes, reliques, potions), dégâts en plus grâce à tes affaiblissements, dégâts évités grâce à eux, PV perdus (ennemis, toi-même, alliés), PV max gagnés / perdus pendant la partie |
| **Protection** | Dégâts vraiment arrêtés : armure, familier, reliques/pouvoirs ; armure qui n'a rien arrêté ; le détail par source d'armure (« Défense : 18 arrêtés · 5 inutile ») |
| **Affaiblissements** | Chaque affaiblissement posé, en nombre d'applications / cumuls au total |
| **Occasions manquées** | Tours où tu pouvais tuer, armure gâchée, choix moins bons (attaque plus faible, mauvais ordre…), énergie perdue, PV évitables, effets de relique manqués à une carte près, potions gardées à ta mort — chacun avec une courte explication |
| **Coop** | Armure donnée, PV sauvés aux alliés, dégâts encaissés à leur place, dégâts permis aux alliés, énergie et cartes données, tirs amis, morts, chifumi pour les reliques |
| **Feux de camp** | Qui a choisi quoi à chaque feu de camp |
| **Cartes et potions** | Piochées, jouées, épuisées, défaussées, potions bues |

Chaque chiffre suit une règle écrite : l'armure est consommée de la plus ancienne à la plus récente, une aide ne
compte que si elle a vraiment sauvé des PV ou fait des dégâts, une carte gardée exprès (Retenue, Étoiles,
Épuisement…) n'est jamais une erreur. Les règles complètes (en anglais) sont dans
[docs/specs/01-metrics.md](docs/specs/01-metrics.md).

#### Les autres fenêtres
- **Résumé du combat :** un graphique, tour par tour, des dégâts, PV perdus, affaiblissements ou PV sauvés, pour
  chaque combat de la partie.
- **Trophées :** à la fin de la partie (ou à tout moment), le meilleur joueur de chaque catégorie, avec les records
  absolus, les records perso et les records par personnage. Records solo et coop séparés.
- **Compositions de groupe** (F8, aussi dans le salon) : combien de fois chaque groupe de personnages a été joué et
  gagné, par niveau d'ascension, pour toutes tes parties ou seulement ce groupe de joueurs ; un tableau de qui a joué
  quoi ; une composition jamais jouée ou jamais gagnée à essayer. Construit à partir de l'historique du jeu.

#### Paramètres (⚙)
Langue (English / Français), sections et colonnes affichées, masquer les colonnes à 0, mettre une section dans sa
propre fenêtre, afficher le résumé à la fin de chaque combat.

### Touches (modifiables dans les réglages, ou depuis l'écran Mods du jeu)
- **F7** : afficher / masquer le panneau
- **F8** : compositions de groupe
- **F9** : signaler un bug, une stat fausse ou une idée (aussi dans les paramètres). Le mod crée un zip avec une capture
  d'écran, l'état de la partie et son journal, sans les noms des joueurs ni les ids Steam, puis ouvre une page GitHub :
  glisse le zip dessus.

### Installation
**Steam Workshop** (le plus simple, mises à jour automatiques) : abonne-toi à
[KikiMAIster](https://steamcommunity.com/sharedfiles/filedetails/?id=3832591950), puis lance le jeu. Depuis la 1.1.0, il
marche sur les deux branches du jeu (bêta v0.113.0 et par défaut v0.111.0 ; la 1.2.0 est une première mise à jour rapide pour la nouvelle bêta, d'autres suivront vite).

Installation manuelle, jeu fermé :
1. Télécharge le zip de la version (`KikiMAIster.dll` et `KikiMAIster.json`).
2. Mets les deux fichiers dans `<bibliothèque Steam>\steamapps\common\Slay the Spire 2\mods\KikiMAIster\`.
3. Lance le jeu et active le mod dans le menu des mods si besoin.

Un seul joueur a besoin du mod : le jeu de chaque joueur simule tout le combat, tu vois donc les stats de tout le
monde.

Le mod garde ses fichiers dans le dossier utilisateur du jeu (`%APPDATA%\SlayTheSpire2\KikiMAIster` sous Windows),
les mises à jour du mod ne les effacent donc jamais : paramètres, records, compositions, résultats du chifumi, fichiers de
partie (`runs/`, pour garder les stats après « Sauvegarder et quitter ») et `KikiMAIster.log`.

### Compatibilité
Fait pour Slay the Spire 2 (accès anticipé) : une seule version pour les branches **par défaut** et **bêta**. Une mise à jour du jeu peut casser certains
branchements ; le mod rattrape ses propres erreurs pour ne jamais faire planter le jeu.

### Licence
[MIT](LICENSE) © Zaille75. Slay the Spire 2 appartient à Mega Crit ; aucun fichier du jeu n'est inclus.
