# Changelog

Summary by version line. The detailed notes of every version are in [docs/specs/02-ui.md](docs/specs/02-ui.md)
(interface) and [docs/specs/01-metrics.md](docs/specs/01-metrics.md) (rules).

## Unreleased (towards 1.0)
- README (English / French), MIT license, icon.

## 0.17 — co-op feedback, pets, compositions, campfires
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
