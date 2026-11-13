# Changelog

Summary by version line. The detailed notes of every version are in [docs/specs/02-ui.md](docs/specs/02-ui.md)
(interface) and [docs/specs/01-metrics.md](docs/specs/01-metrics.md) (rules).

## 1.2.1 (draft, not released) — the Fool's arrows
The Fool's arrow cards, from the game code. Fixes: a Shot with no arrow ready is no longer a "free card" (Quick Shot), a
block alternative, a Stars / Carapace alternative or the "stronger attack"; Nail with no arrow in any enemy and Recall /
Uproot / Wrest with none of the player's arrows in an enemy do nothing and are never suggested; lethal checks and Paré au
combat count no more Shots than the arrows ready. New mistakes: Nail / Recall / Uproot / Wrest on a target without an arrow
(Gâché), a payoff card (Bow Smack, Ki Hop, Wah Hammer, Nail) played before the Shot that impaled its target (Choix), a Shot
whose arrow stayed stuck while 3 earlier hits would have freed it (Choix), Quietus with no shot after it (Gâché); wrong
order for Cho Firm / Whistling Arrows after a Shot, Aim spent on a weaker Shot, attacks before the impaling Shot with
Serrated Arrows. An empty Shot that completed a relic count (Kunai…) is not a mistake. Hobbling Shot counts for "Vulnérable
gardé". From the 2026-11-13 games: arrows in co-op (an ally's Shot counts for "impale first" and Serrated
Arrows), a Shot is kept on purpose with Visée against Glissant, card makers (Abondance…) should be played first ("makerFirst"),
Feu de couverture after a Shot ("coveringFireLate"), a Galvanisé card is kept on purpose (it costs HP), l'Insensé is yellow,
the end-of-run title shows the ascension, and the panel no longer closes the hover text at every card played.

## 1.2.0 (2026-11-13) — first quick update for the beta v0.113.0, more to come
Game update: works with the beta v0.113.0 (the Fool, Blitz, Dread) and the main branch v0.111.0. Damage and kills are
tracked again on the new beta; multi-hit attacks count all their hits again in the misplay estimates (the game renamed
their hit count). Paré au combat is no longer treated as an energy-only card. Blitz and Dread have their own records,
fight records and compositions (F8: new "Mode" row). The Fool: a Shot played with no arrow ready is a new mistake, and such
a Shot is never suggested as a card to play. Wuppy counts like Titos, and a Tisse-Mort with both keeps each pet's stats
apart. Paré au combat is never "gâché" (its cost cut is a side effect); keeping it when it would have paid for more attacks
is a new "Choix" mistake. Blitz: no Or section and no gold trophies. New trophy Pie voleuse (most relics).

## 1.1.1 (2026-11-06)
Misplays: new "Vulnérable gardé" (an attack giving Vulnérable, like Tope là, kept while plain attacks were played);
wrong order against Ralenti and before Vulnérable take Létalité, Vigueur, Pointe de stylo… into account; upgraded cards
show their "+". Co-op: block from Givre orbs under Hibernation counts for the Défectueux who owns the orbs. The GitHub
report form says where the zip is and asks to wait for its link.

## 1.1.0 (2026-11-03)
One build for both game branches (default and beta): the mod finds the branch when the game starts. One Steam Workshop
item, [KikiMAIster](https://steamcommunity.com/sharedfiles/filedetails/?id=3832591950). A patch that fails no longer stops the others from loading.

## 1.0.2 (2026-11-02)
The log says again where the mod's data folder is (its line was empty in 1.0.1). Workshop pages and README: subscribe to
only one of the two Workshop items (same mod id: the game loads only the first one).

## 1.0.1 (2026-11-02)
The mod's files (settings, records, compositions, chifumi, runs, reports, log) move to the game's user folder
(`%APPDATA%\SlayTheSpire2\KikiMAIster` on Windows), so a mod update, from the Steam Workshop or by hand, never erases
them. The files of 1.0.0 are copied there the first time it starts. New icon.

## 1.0.0 (2026-10-31)
First public version: v0.18.10 as it is, released as 1.0.0. README (English / French), MIT license, icon.

## 0.18 — towards 1.0
- **0.18.10:** card-by-card review of the co-op log: a won run no longer ends a second time as a defeat (the game kills
  the players to close it: an extra death each, a potion error); no free card or relic error for an attack while an enemy
  sleeps (Matriarche lagavulin); En boule: a smaller hit before the big one no longer hides the miss; an enemy's powers
  removed at its death no longer lose the poison kill's credit.
- **0.18.9:** co-op runs feedback: no wrong order into a cap (Glissant, Dur à cuire), one per turn; no weak attack for
  Vague de fer / played-again cards; Impatience, free discard cards, Éventail décoratif, La grande manœuvre, random
  follow-ups and reaction kills no longer false errors; discards count only when something reacts; new errors: lethal
  missed at the end of the turn (hands together), Ethereal curse discarded, Garde du corps after Déchaînement, cards
  before Forme du serpent, small hit setting off En boule / Pleutre; debuffs reset with an enemy's new form; Précision in
  Pouvoirs; Totaux with damage into block (PV / Bloqués); "Malus"; card owners in co-op errors; chifumi table; hotkeys
  in the settings and the game's Mods screen; participation ignores fun trophies; Distrait records reset.
- **0.18.8:** a useless card played with nothing else to play counts again as energy spent for nothing (a stat, not a
  mistake); new errors: attack into a damage cap a cheaper one matched (if the saving paid something useful), Ruée swap
  (expensive damage-only attack paid while Ruée played a cheaper one for free), draw before creating cards into a full
  hand.
- **0.18.7:** Fatalité x/y (useless / applied) in its own Affaiblissements column; review of the last logs: cards Souillés
  (Prisme infesté) no longer count as energy lost or block kept; full hand no longer counts the Surins a played-again
  Passe-passe created.
- **0.18.6:** every tooltip explains only its own column (PV Soins column, debuffs received in Affaiblissements);
  wrong order counted once per card and turn, not for cards its power fed (Image rémanente, Lames fantomatiques); new
  errors: Ruée, Pas de pioche, full hand, potion with no possible effect; Fatalité x/y (useless / applied) and Doom-only cards'
  energy; Domaine counted in Énergie gagnée; energy left with only kept cards, or a useless card with nothing else to
  play, counts as surplus; Ce n'est qu'une égratinure needs the turn to end at 1 HP; Record absolu "value holder (date)".
- **0.18.5:** fix: the mod no longer stays hidden after "Sauvegarder et quitter" then "Continuer"; new PV section,
  Défense (with Debuff), Erreurs de jeu, renamed columns, Cartes last; Énergie gagnée and Énergie inutile x/y; each
  value's tooltip shows only its column; more wrong-order cases (Calcification, Décomposition, Monologue, Forge…);
  Activation double kept on purpose; solo trophies: Record <personnage> then Record absolu, values highlighted when
  they beat a record; date format setting; Batterie pleine becomes Survolté.
- **0.18.4:** "Cartes" and "Potions et énergie" sections; cards that cost HP (Percée…) kept on purpose; "Énergie pour
  rien" no longer counts two block cards when only one was too much; no false weak attack with Piétinement; clearer
  event lines.
- **0.18.3:** solo: no character name on the trophy lines.
- **0.18.2:** solo: simpler trophies window (no winner / personal best columns, character named once); no co-op
  settings.
- **0.18.1:** event outcomes (option chosen, cards, relics, potions, gold, HP) and shared co-op event votes written in
  the log; no new stat.
- **0.18.0:** F9 (or the settings button) makes an anonymised report zip and opens the GitHub issue form; the panel
  shows a ⚠ line when the mod noted an internal error.

## 0.17 — co-op feedback, pets, compositions, campfires
- **0.17.45:** relics get their share of damage and block (Reliques), Soufre's enemy Strength charged to its owner,
  caps in Excédent; co-op Vulnérable order; misplay fixes (dead pet, energy-only cards, Détonation osseuse, Orichalque,
  attacks into caps); card rewards; Chifumi section and records; tied trophies, fun section, 8 new trophies; F7 / Échap
  hide every window; names before the first fight; Panier percé agrees between players; clearer play-by-play log.
- **Compositions:** every composition listed, sorted by games or win rate; "this group of players" stats from the
  game's run history; a table of who played what (2 players: my character × my partner's; 3–4 players: each player ×
  character); never-played / never-won suggestions with a random pick; opens by itself in the lobby, can be folded.
- **New stats:** useless block split into avoidable / unavoidable ("29/12"), HP lost and healed outside fights, gold and curses (trophies Chercheur d'or, Panier percé, Radin,
  Chat noir), campfire choices, Max HP gained / lost (trophies Culturiste and Peau sur les os), rock-paper-scissors
  for relics across runs, self-inflicted damage, block enabled for allies (Sacrifice), campfire heals for allies.
- **Pets:** HP saved on a pet (by debuffs or block) only count once it dies and its owner loses HP; attacks that grow
  with the pet's HP credit the allies behind those HP.
- **Rules:** cards kept on purpose (Retain, Star cost, Exhaust) are never counted as not played; Slow, Strength drop
  (Friendship) and Plating handled; Personal Hive treated like Thorns; Miniature Tent options left behind; debuff
  help only counts the HP really saved or dealt; missed lethal ignores enemies spawned by a kill; block from a card that
  had an alternative absorbs last (Frost orbs make a Defend gâché); Focus gained after orbs fired and a rest before the
  boss that was not needed count in Choix (Pantographe included); Ice Lance no longer counted as 3 hits; Stars spent for
  nothing or where energy would do; attacks stopped by Carapace endurcie when something better was playable; Exhaust
  cards never a miss when kept; Rage, asleep enemies and Thorns handled for unused energy and free cards.
- **Code review (0.17.38):** growing cards never played are counted at last; Sacrifice no longer counts as protection;
  a player dead during their own card no longer "acts" for the rest of the fight; cards played again (Déflagration…) are
  one choice; Intangible / Dur à cuire in Évités; Strength down counted whatever the enemy's own Strength; co-op incoming
  attack computed for each player; status and curse damage go to whoever created the card; kills once per enemy; heals
  after fights and the Ancients' heals in "Soins hors combat" (Neow left out); Oubli's block enabled for the giver;
  Paresse, Résonnant, Entraves and extra turns handled.
- **0.17.39–0.17.41:** enchantment damage, HP lost by kind and heals by source in the details; fight length and fastest
  wins per encounter across runs with a "Nouveau record" notice; the build fails and the log warns when a private game
  member the mod reads is renamed; the misplay rules moved to the tested core (no change in game) and a per-fight log
  count of damage / block whose source was not found.
- **0.17.42:** overkill column (Excédent, avoidable / unavoidable) with Bulldozer and Chirurgien; big attack wasted on an
  enemy a smaller one or Trou noir would have killed (Gâché); Slow: bigger attack before a smaller one, allies included;
  Max HP gained after a capped heal (new "Hors combat" misplays); fight time stops at the last kill; HP outside fights
  shown again (history read fixed); trophy hover texts to the right of the mouse.
- **0.17.43:** play-by-play log of each fight (turns, cards, targets, energy, effects), readable in co-op.
- **0.17.44:** status and curse damage credited to the card's creator for real; cards costing other hand cards (HORS DE MA VUE !…) kept on purpose.
- **Details and records (0.17.39):** enchantment damage, HP lost by kind and heals by source in the details; fight length
  (rounds, time) and fastest-win records per encounter across runs (ascension, players, solo character), with a notice.
- **Interface:** "Erreurs" (Gâché + Choix) in every tab, sections in their own windows, hide columns at 0, richer details (Protection by block source),
  clicked window comes to the front, draggable settings, visible checkboxes, explanations kept across Save & Quit,
  previous session's log kept.

## 0.16 — group compositions
- Games and wins per group composition and ascension level, never-played / never-won suggestions (F8, also in the
  lobby), rebuilt from the game's run history.

## 0.15 — co-op trophies
- Fortress counts your own protection only; new co-op trophies (Human shield, Dynamo, Card dealer); Tank and pets
  given to allies credited; per-ally help in the details; solo and co-op records kept apart.

## 0.14 — first long solo run
- Damage taken merged into the Damage section, debuff effect split (extra damage / avoided), misplays column,
  wrong-order misses, previous fights kept across Save & Quit, records per character, trophy Trump card.

## 0.10 – 0.13 — missed opportunities and co-op credits
- Missed lethal, wasted block, weaker choices, unused energy, avoidable HP, relic triggers, potions left; real hit
  counts for every attack; "enabled" damage for allies (Vulnerable, Flanking, created cards, Tag Team…); energy,
  cards and orbs given; damage taken for allies.

## 0.6 – 0.9 — summaries and trophies
- Combat summary chart, combat browser, settings window, end-of-run trophies with all-time records, co-op layout.

## 0.1 – 0.5 — first versions
- Capture of damage, block (oldest first), debuffs and damage over time per player; live panel with turn / combat /
  run tabs; English and French; run totals kept across Save & Quit.
