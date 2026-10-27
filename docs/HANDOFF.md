# Handoff — state of the project (2026-10-27, v0.7.3)

Read this first in a new conversation, then `CLAUDE.md`, `docs/specs/01-metrics.md` and `docs/specs/02-ui.md`.
The next conversation is about **improving the layout and fixing bugs**.

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

## Code map
| File | Role |
|---|---|
| `src/KikiMAIster.Core/` | Pure C#, unit-tested. `Events.cs` (all event records), `StatsEngine.cs` (scopes Turn/PreviousTurn/Combat/Run, block FIFO, wasted vs useless, timeline), `PlayerStats.cs`, `BlockLedger.cs`, `DotLedger.cs`, `Timeline.cs`, `RunSnapshot.cs` |
| `Capture.cs` | Emit events, actor mapping, `CallingModelType()` stack walk, `SourceOf`, `CurrentActingPlayer`, `PlayerName` |
| `Patches/CombatFlowPatches.cs` | Run/combat/round boundaries, run restore (RunManager.SetUp*), card play start/finish, "other effect" hooks |
| `Patches/DamagePatches.cs` | Damage capture, including pending hits for the killing blow, DoT attribution, HP-loss modifiers (Tungsten Rod…) |
| `Patches/BlockAndPowerPatches.cs` | Block, powers/debuffs (temporary Strength down), heal, pet summon |
| `Patches/BlockAlternatives.cs` | gâché vs inutile decision at end of each player turn (rules F3–F6) |
| `Patches/UiPatches.cs` | Overlay attach (first input / combat start), F7, hide auto summary on map |
| `UI/Overlay.cs` (~580 lines) | Live panel: scope tabs (Tour préc. / Combat / Partie), sections Dégâts / Protection / Affaiblissements / Subis / Tirs amis, tooltips, solo vs co-op tables, keep-on-screen, Summary + language buttons |
| `UI/CombatSummary.cs` | Draggable chart window (cumulative per round), combat picker, metric buttons, auto-show checkbox |
| `UI/Loc.cs` | All EN/FR strings |
| `UI/GameNames.cs` | Localized card/power/relic names from the game's own models |
| `ModConfig.cs` | Saved settings (position, scope, language, auto summary, summary position) |
| `RunStore.cs` | Run totals saved per profile/seed/ascension/players in `runs/*.txt` |

## Godot UI lessons (avoid re-learning them)
- Use built-in nodes only (no custom scripts). Containers grow but never shrink by themselves: `RemoveChild` the content, then `CallDeferred(ResetSize)`.
- Don't rebuild an `OptionButton` while its popup is open. Set a flag and rebuild on the next timer tick.
- Charts use `Line2D` inside a `Control`.
- The overlay is attached lazily (`Overlay.EnsureAttached`), because a deferred call from the mod initializer never ran.

## Status of the latest tests
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
- Zaille75 has **several layout points and bugs to discuss** after the first co-op game. Ask him for them; he hasn't listed them yet.
- Roadmap leftovers:
  - MVP line in the summary;
  - end-of-run summary (Totals / Per act / Records);
  - JSON export;
  - settings UI (opacity, scale, hotkey);
  - support for the default (non-beta) branch (`lib/default`);
  - E1 missed-damage estimate and unused energy;
  - Vulnerable/Weak attribution (v2);
  - publishing (GitHub, Steam Workshop, Nexus).
- Offered but not requested: keeping the chart history (per-combat timeline) across Save & Quit. Today only run totals survive it.
