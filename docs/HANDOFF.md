# Handoff — state of the project (2026-10-27, v0.16.1)

Read this first in a new conversation, then `CLAUDE.md`, `docs/specs/01-metrics.md` and `docs/specs/02-ui.md`.
The conversation of 2026-10-27 turned the 27 feedback points of the first co-op games into v0.8.0 → v0.13.4, then
refined the rules with Zaille75 (card and relic audits). **Only one short solo run (v0.13.3, 3 fights, Soldat de fer)
was played since v0.8.0: no errors; it revealed the En miettes hit-count bug fixed in v0.13.4. The co-op parts are
not tested in game yet.**
**Then a 13-fight solo run (v0.13.4, Ironclad, death on fight 13 against Dix-Mille-Pattes), no errors: its 16 feedback
points became v0.14.0** (merged Dégâts/Subis, split debuff effect, Erreurs column + fight numbers in the Run tab,
wrong-order misses, Waterfall Giant invulnerable phase, fights kept across Save & Quit, per-character records, trophy
"Carte maîtresse", integer chart axis, trophies window keeps clear, settings grid, language applies to every window).

## How Zaille75 and Claude work
- Zaille75 writes in English or French, plays in **French**, and doesn't code. He tests in game and sends feedback, screenshots, and logs.
- **Log transfer:** when Zaille75 says "log transferred", he has copied his Windows log over `logs/KikiMAIster.log` on this machine.
  - Read it by grepping: `EVENT <Type>`, `===== COMBAT SUMMARY`, `block play N:` lines, and errors.
  - Then archive it as `logs/<date>_v<version>_<short-topic>.log`. `logs/` is git-ignored because it contains friends' Steam IDs.
- **Every change:**
  1. Update the spec first (`01-metrics.md` for rules; `02-ui.md` for the UI, with a "vX.Y.Z:" change note).
  2. Bump the version in **both** `src/KikiMAIster/KikiMAIster.json` (`"version"`) and `src/KikiMAIster/KikiMAIster.csproj` (`<Version>`).
  3. Run `./build.sh beta`. It runs the build, PatchCheck (each patch must match exactly one game method), and the unit tests. Then commit.
  4. Give Zaille75 the install command (PowerShell, game closed):
     ```powershell
     $m = "<game folder>\mods\KikiMAIster"
     scp "<user>@<server>:projets/KikiMAIster/dist/beta/KikiMAIster/*" "$m"
     ```
- The mod writes `KikiMAIster.log` next to its DLL, in the game's `mods\KikiMAIster` folder on Windows. Zaille75 copies it from there.
- **Git:** local only (`main`, no remote yet). Publishing to GitHub needs `gh auth login` by Zaille75 and a public/private choice.

- **Names:** talk to Zaille75 with the **French in-game names** of cards, relics, powers, potions. The table is
  `lib/beta/names.txt` (copied from the mod's `KikiMAIster.names.txt`; lines `kind|C# class|ID|name`; game text,
  kept out of git like the rest of `lib/`). Look names up there before writing to Zaille75.

## Rules validated by Zaille75 in this conversation (details in the specs)
- **Attribution principle:** every damage, base and extra, counts for the **attacker** (Dégâts). The extra caused by
  another player's effect is tracked **in addition** as "Permis" (co-op section) for that player: Vulnérable (current
  turn, FIFO), Harcèlement / Domination (by remaining Vulnérable turns), Encerclement, Renverser, Un pour tous,
  Embrasement, Coordination, Décoction (the Poison stays the attacker's), cards created for an ally, Relais.
- Vulnérable / Affaibli: first in, first out, turn by turn. Strength −: by stacks. Potion-applied powers count as Potion.
- Gâché (block rule F3) + side effects F4 (audit of all 80 block cards), Épines rule F7, useless debuffs F8 (1 Affaibli
  on a non-attacking enemy, 1 Vulnérable nobody uses). Sacrifice is never gâché.
- "Occasions manquées" columns are **counts of misplays**: Létal | Gâché (wasted block plays + growing cards never
  played in a fight) | Choix | Énergie | Évitables | Reliques | Potions. Relic triggers only count when the extra card
  was worth playing (energy lost anyway, Épines, block needed). Larmes de Paël: energy spent on a useless card.
- Co-op help stats: block given, HP saved, Encaissés (Interposition), Permis, energy, cards, orbs, friendly fire, deaths.
- Trophies (list still a proposal): includes Roi du combo (most cards in one turn) and Coup de massue (biggest hit,
  block and overkill included).
- Panel and windows only during a run (not at start-up / main menu).

## Code map
| File | Role |
|---|---|
| `src/KikiMAIster.Core/` | Pure C#, unit-tested. `Events.cs` (all event records), `StatsEngine.cs` (scopes Turn/PreviousTurn/Combat/Run, block FIFO, wasted vs useless, timeline), `PlayerStats.cs`, `BlockLedger.cs`, `DotLedger.cs`, `Timeline.cs`, `RunSnapshot.cs` |
| `Capture.cs` | Emit events, actor mapping, `CallingModelType()` stack walk, `SourceOf`, `CurrentActingPlayer`, `PlayerName` |
| `Patches/CombatFlowPatches.cs` | Run/combat/round boundaries, run restore (RunManager.SetUp*), card play start/finish, "other effect" hooks |
| `Patches/DamagePatches.cs` | Damage capture, including pending hits for the killing blow, DoT attribution, HP-loss modifiers (Tungsten Rod…) |
| `Patches/BlockAndPowerPatches.cs` | Block, powers/debuffs (temporary Strength down), heal, pet summon |
| `Patches/BlockAlternatives.cs` | End of each player turn: gâché vs inutile (rules F3–F7, Thorns), missed lethal (front hand, checked at `AfterPlayerTurnStart`), weak attacks; damage estimates via `Hook.ModifyDamage` |
| `Patches/DebuffEffectPatches.cs` | Vulnerable / Weak / Strength− effect per real hit (`Hook.ModifyDamage` postfix, confirmed by `AfterModifyingDamageAmount`) |
| `Core/FifoLedger.cs`, `Core/Lethal.cs`, `Core/Trophies.cs` | Duration-debuff FIFO, lethal solver, trophies + record book |
| `BlockAlternatives.HitCount` | Hit count of every attack (CalculatedHits, Repeat, fixed two-hit list, unknown list) — keep in sync after game updates |
| `BlockAlternatives.CheckOrder/ResolveOrder` | Wrong-order misses (Vulnerable / Strength / Dexterity applied after plays they would have boosted) |
| `Patches/UiPatches.cs` | Overlay attach (first input / combat start), F7, hide auto summary on map |
| `UI/Overlay.cs` | Live panel: scope tabs, ◂ ▸ previous combats, sections Dégâts / Protection / Affaiblissements / Occasions manquées / Subis / Coop / Autres, players sorted best first, details under the player's line, hidden columns from settings |
| `UI/SettingsMenu.cs` | ⚙ window: language, auto summary, shown sections/columns (`ModConfig.Hidden`) |
| `UI/CombatSummary.cs` | Draggable chart window (cumulative or per round), combat picker, co-op summary, keeps out of the panel's way, Trophées button |
| `UI/CompositionWindow.cs`, `Core/Compositions.cs` | Group compositions (F8, also in the lobby): games/wins per composition, never played / never won suggestions (`KikiMAIster.compositions.cfg`) |
| `UI/RunSummary.cs` | End-of-run trophies window + `RecordStore` (`KikiMAIster.records.cfg`) |
| `UI/Loc.cs` | All EN/FR strings |
| `UI/GameNames.cs` | Localized card/power/relic names from the game's own models |
| `ModConfig.cs` | Saved settings (position, scope, language, auto summary, summary position) |
| `RunStore.cs` | Run totals saved per profile/seed/ascension/players in `runs/*.txt` |

## Godot UI lessons (avoid re-learning them)
- Use built-in nodes only (no custom scripts). Containers grow but never shrink by themselves: `RemoveChild` the content, then `CallDeferred(ResetSize)`.
- Don't rebuild an `OptionButton` while its popup is open. Set a flag and rebuild on the next timer tick.
- Charts use `Line2D` inside a `Control`.
- The overlay is attached lazily (`Overlay.EnsureAttached`), because a deferred call from the mod initializer never ran.

## To check in game (v0.8.0 → v0.13.4)
- Layout: detail box under a player's line (Godot trick: zero-width grid cell, see `Overlay.DetailCell`), tighter buttons,
  settings window, ◂ ▸ combat browsing, summary window moving away from the panel, co-op summary, trophies window.
- Data: Haunt (Max) damage, potion Thorns/Demise under Potions, Waterfall Giant last phase ignored, Armaments / Gather
  Light never gâché, Thorns rule, Vulnerable/Weak/Strength− effects (`Debuff effects:` line in the COMBAT SUMMARY log),
  missed lethal (`missed lethal:` log lines), weak attacks, card counts, deaths, records at run end.
- Co-op credits: Permis from allies' effects, Décoction, Relais, cards given, energy given, Interposition (Encaissés).
- Missed: relic triggers, Larmes de Paël, Orichalque, Pointe de stylo, growing cards at fight end, Évitables.

## Status of the earlier tests
- **Solo (v0.7.1) and co-op (v0.7.2, 2 runs, 2 or 3 players):** no errors. Each player is tracked separately, including friends who don't have the mod.
- **Fixed in v0.7.3, not yet checked in game:**
  - A killing blow now keeps its card. Before, it showed as "Unknown": the game's internal `CreatureCmd.Kill` overwrote the damage call. It is now ignored when the creature is already at 0 HP.
  - Pet HP from a card that summons for everyone is credited to the player who played the card.
- **Never checked in game:**
  - how the chart window renders, and dragging it;
  - the summary auto-hiding when the map opens;
  - the "why gâché" tooltip;
  - Tungsten Rod in Évités;
  - how the co-op layout looks on screen.
- **Known minor issues in the data:**
  - A creature removed by the game with huge HP (e.g. 999999878 in "Géant de la Cascade") is logged as damage from nobody. It is not credited to a player, but it could be filtered out.
  - The last fight of the co-op log (Crabe César) ended as a defeat. Zaille75 hasn't confirmed this.

## Open topics for the next conversation
- Zaille75 tests v0.13.4 in co-op and sends the log ("log transferred").
- **Trophy list:** v0.15.0 co-op additions validated by Zaille75 (Bouclier humain, Dynamo, Croupier; Forteresse = own only;
  Traître includes ally HP that a kept ally block card could have saved; Tank and pets given to allies credited). Not tested in game.
- Chart: "remove the blocked stats" was read as removing the "Évités" chart; Zaille75 said he would check.
- Growing cards were moved from "Choix" to "Gâché" (v0.13.1); Zaille75 may prefer "Choix".
- Previous fights are kept across Save & Quit since v0.14.0 (run file `combat.N.*` lines); not the explanations of misses.
- **Asked Zaille75 (v0.14.0):** the "Agression" feedback. The log showed the only flagged play (Mante Chapardeuse T4: Frappe
  instead of Incendie) with Incendie in hand since the start of the turn (Agression adds it before the draw). Waiting
  for the exact line he saw. Intouchable: victories only, hidden after a defeat (validated, v0.14.1).
- Roadmap leftovers: MVP line in the summary; end-of-run Totals / Per act tabs; JSON export; settings for opacity,
  scale, hotkey; support for the default (non-beta) branch (`lib/default`); publishing (GitHub, Steam Workshop, Nexus).
- Offered but not requested: keeping the chart history (per-combat timeline) across Save & Quit.
