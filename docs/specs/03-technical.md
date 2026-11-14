# 03 — Technical design

**Status:** DRAFT

## Platform facts (from community docs, game v0.103 – v0.107+)
- The game is C# (.NET 9) on Godot 4.5.1. It loads mods natively from `<game>/mods/` and through the
  Steam Workshop ("Play with Mods").
- A mod is `<Id>.json` (the manifest) + `<Id>.dll` (the code) + an optional `<Id>.pck` (assets).
- Code runs from a `[ModInitializer]` entry point and hooks game methods with **Harmony**.

## Choices
| Topic | Decision | Why |
|---|---|---|
| Language | C# / net9.0, SDK-style csproj | Same as the game. |
| Dependencies | **Harmony only** (shipped with the game). **No BaseLib.** | Fewer breakages; BaseLib has had crash reports on the Workshop. |
| Assets | Code-only at first (`has_pck: false`). UI built in code with Godot nodes. | No Godot editor needed. A `.pck` is added only if we need icons or fonts. |
| `affects_gameplay` | `false` | Read-only mod; other players should not need it. |
| Build machine | Linux dev box (`dotnet build`) | Game DLLs are copied from the Windows PC to `lib/<branch>/`. |
| Testing | Unit tests on the stats engine (no game needed) + manual in-game tests by Zaille75 on Windows. | |

## Architecture (layers)
1. **Capture** (`Patches/`): thin Harmony patches that turn game events into neutral `StatEvent`s
   (DamageEvent, BlockEvent, PowerAppliedEvent, HealEvent, CombatStart/End, TurnStart, RunEnd…).
   This is the **only** layer that depends on game internals, which keeps the cost of game updates small.
2. **Stats engine** (`Core/`): pure C#, no Godot or game types. It aggregates events into per-player
   stats at turn, combat and run scope. Unit tested against the definitions in `01-metrics.md`.
3. **UI** (`UI/`): Godot nodes that read the engine's state; overlay plus summaries.
4. **Persistence** (`Core/`): saves the current run's stats so Save & Quit / Continue doesn't lose
   them, plus the end-of-run JSON export.

## Capture layer (Phase 2, game v0.111)
| Event | Game hook | Notes |
|---|---|---|
| Run / combat start | `Hook.BeforeCombatStart` | New `IRunState` object = new run (persistence across Save & Quit: Phase 4). |
| Round start | `Hook.BeforeSideTurnStart` (player side) | |
| Block cleared at turn start | `Hook.AfterBlockCleared` → `BlockObserved(actual block)` | Handles Barricade-like effects (block kept). |
| Combat end | `Hook.AfterCombatEnd` | Remaining block → wasted / useless help. |
| Damage | `CombatHistory.DamageReceived` + `CreatureAttacked` + `Creature.LoseHpInternal` (fallback) | Deduplicated per `DamageResult`; fallback covers the final hit the history skips. |
| Damage source | `CreatureCmd.Damage` prefix: card, else **call-stack walk** to find the calling power/relic/orb/potion | Debuff that damages its own holder = damage-over-time, credited by stacks. |
| Block | `CreatureCmd.GainBlock` prefix (giver) + `CombatHistory.BlockGained` (final amount) | Giver: card owner › potion user › single owner of the power/relic › receiver. |
| Block removed | `CreatureCmd.LoseBlock` | Removed by the player itself = "spent", otherwise wasted. |
| Powers | `CombatHistory.PowerReceived` | |
| Healing | `CreatureCmd.Heal` | Healer = player playing a card / using a potion, else the healed player. |

Rule learned in the spike: patch the public `CombatHistory` wrappers, never the private `Add` (JIT inlining).
`tools/PatchCheck` (run by `build.sh`) verifies that every patch matches exactly one game method.
**v0.17.40:** private game members read by name (`Hook.ModifyDamageInternal`, `PenNib.AttackToDouble`,
`RestSiteSynchronizer.LocalPlayer`, `SoulboundPower.IsAddingSoul`) are all listed in `src/KikiMAIster/ReflectionTargets.cs`
(type as text, so the list loads no game code). PatchCheck fails the build when one is missing, and the mod logs an
`ERROR game member not found` line at start-up (the stat using it stays empty). Any new reflection must go through that
list.
**v0.17.41:** the end-of-turn misplay rules (wrong order, weak attacks, energy left, block cards kept, relic triggers,
free cards, wasted block, Stars, Carapace endurcie) live in `Core/TurnReview.cs` and are unit-tested
(`TurnReviewTests`). `Patches/BlockAlternatives.cs` only follows the turn (plays, hits, order losses) and, when the players'
turn ends, fills a `TurnState` snapshot (hand cards with the game's cost, block, damage, Thorns, playability; each
player's energy, block, incoming attack, relics) and emits what `TurnReview.Resolve` returns. The per-turn state is
cleared in one place (`ClearTurn`). Missed lethal and the estimates (damage, block, hits) stay in the patch: they call
the game.
**Unknown sources (v0.17.41):** damage and block sources come from a call-stack walk that only works before the game
command's first `await`. A game update adding one would silently turn sources into "Unknown", which PatchCheck cannot
see; so each fight's end logs `sources unknown this fight: N player damage, M block (first: …)` when there were any.
A jump of that count after a game update means the source lookup broke.

**Play-by-play log (v0.17.43, Zaille75):** `PlayLog.cs` writes each fight in the mod's log so every stat can be checked
afterwards against the cards really played, solo or co-op: `═══` fight start (players, HP); `+` each enemy when first
seen (`Larve#7`: the number tells two of a kind apart); `───` each round's enemies (HP, block, powers, intent with the
attack total on each player) and each player's turn (HP, block, energy, Stars, powers, pet, hand, piles); `▶` card played
(player, card title + id, target, energy / Stars paid and left, auto-play, played again); `◀` its summary (damage, block,
overkill, kills, powers, heals, misses during it); `⚗` potion and target; `■` each player's end of turn (energy, Stars,
block, incoming attack, cards kept). Every EVENT line names creatures the same way (`Actor.Namer`); the raw card events
(drawn, played, finished) are no longer written.

## Reports (v0.18.0)
F9 / the settings button write a report the player sends by hand (no network call from the mod, no secret in the DLL).
- **Files:** `message.txt` (type, date, mod version, game branch, text), `state.txt` (game state at F9), `run.txt` (the
  run's stats), `log-tail.txt` (last 600 lines of `KikiMAIster.log`), `errors.txt` (each internal error noted this
  session: place, count, first full text; at most 30), `screenshot.png` (optional), all zipped into
  `feedback/KikiMAIster-report-<date>_<type>.zip`.
- **Anonymisation (`Core/Anonymizer`, unit-tested), on every text file before writing:** every player name and Steam id
  seen this session (run players, lobby players) or kept in the chifumi records becomes "Joueur 1", "Joueur 2"… in the
  order seen (ids: "id-joueur-N"); any other 17-digit Steam id becomes `[steam-id]`; Windows profile paths `C:\Users\<name>\`
  become `C:\Users\[user]\`; the Windows account and computer names become `[user]` / `[pc]`. Names shorter than
  3 characters are left alone, so they cannot replace parts of ordinary words. The screenshot cannot be anonymised: the window warns about it and
  the player can leave it out.
- **Public builds** open the folder (`OS.ShellShowInFileManager`) and the issue form
  `https://github.com/Zaille75/KikiMAIster/issues/new?template=report.yml&…` (fields `kind`, `version`, `branch`,
  `description`; message cut to 1,500 characters in the link). The form is `.github/ISSUE_TEMPLATE/report.yml`.
- **Save only (v1.1.1, Zaille75):** when the file `send-to-claude` exists in the data folder, F9 only saves the report
  (folder + zip), like development builds: no file manager, no GitHub form. `Envoyer-a-Claude.ps1` creates that file each
  time it runs, so only the developer's PC has it; the reports are sent by that script.
- **Error capture:** `ModLog.Error` also records the error in `ModLog.Errors` (by place: count, first text, thread-safe,
  at most 30 places). `ModLog.UnreportedErrors` drives the panel hint; creating a report sets it back to 0.
- The DLL embeds its debug info (line numbers in errors.txt) with the source paths mapped to `/KikiMAIster/`
  (`PathMap`): no folder of the build machine is left in it.

## Data folder (v1.0.1)
- The mod's files live in **`<game user folder>/KikiMAIster/`** (Godot `OS.GetUserDataDir()`, the folder that holds the
  game's saves; on Windows `%APPDATA%\SlayTheSpire2\KikiMAIster`), not next to the DLL: a Steam Workshop update replaces
  the item's folder and would wipe them. Files: `KikiMAIster.settings.cfg`, `.records.cfg`, `.fights.cfg`,
  `.compositions.cfg` (1.2.0: `.records/.fights/.compositions.blitz.cfg` and `.dread.cfg`), `.chifumi.cfg`, `runs/`, `feedback/`, `KikiMAIster.log` + `.previous.log`, `KikiMAIster.names.txt`.
- **Move from older versions:** when that folder does not exist yet, it is created and the `*.cfg` files and the `runs/`
  and `feedback/` folders next to the DLL **and in `<game>/mods/KikiMAIster/`** (a Workshop copy has nothing next to its
  DLL) are copied into it (never overwriting; the old files stay). The log says how
  many were copied and where the folder is.
- If the user folder cannot be used, the mod falls back to the DLL folder (as before).
- The Workshop copy and a manual copy in `mods/` share the same data.

## Safety rules
- Every patch is wrapped in try/catch and logs errors; the mod must **never** crash the game.
- The mod never modifies game state.
- If a patch target is missing (after a game update), that stat is disabled and a warning is
  logged; the rest of the mod keeps working.

## Game versions (beta + default)
- `lib/beta/` and `lib/default/` hold the game DLLs for each branch.
- One DLL for both branches (v1.1.0, see "Game branches").
- `docs/COMPATIBILITY.md` tracks which game versions were tested.

## Risks to verify in the spike (Phase 1) — see GAME-INTERNALS.md
- R1: Does each client see damage events for **all** players (local simulation)?
- R2: How can we link a damage instance to its **source** (card, power, orb, relic)?
- R3: Can we read block **owner/giver** when block is absorbed, so we can compute "prevented for allies"?
- R4: Where do we hook the rewards and end-of-run screens?

## Game branches (v1.1.0: one build for both)
One DLL runs on both game branches (before 1.1.0: one build per branch, `#if GAME_DEFAULT`). Why: both Workshop items
had the same mod id, so the game loaded the first one in its list whatever the branch.
- The mod is compiled against `lib/beta/` only: `./build.sh` → `dist/KikiMAIster/`, release zip
  `dist/release/KikiMAIster-<version>.zip`.
- `GameBranch.IsV113` is read once at start-up from the game's own API (`Hook.ShouldTakeExtraTurn` has 3 parameters on
  the beta v0.113.0); `GameBranch.Name` ("beta" / "main") goes in the log's first line and in F9 reports. (Before 1.2.2:
  `GameBranch.IsBeta`, `CardPlay.Player`, told v0.111.0 from the old main v0.107.1.)
- Patches whose target differs: one patch class per branch with `Prepare()` returning the branch check (Harmony skips the
  other), or `TargetMethod()` picking the right method. Each patch class is applied on its own: one failing patch is
  logged and the others still apply.
- Calls whose signature differs (`Hook.ShouldTakeExtraTurn`): `GameApi` wrappers; the beta call sits in a method marked
  `[BranchOnly]`, the main branch's is a delegate made by reflection.
- Game types / members that exist on one branch only are named only inside methods marked `[BranchOnly]`, called only
  after a `GameBranch.IsV113` check (the runtime resolves a method's references when it first runs it).
- Old main v0.107.1 differences (support dropped in 1.2.2, the code for them removed): turn end hook `Hook.BeforeTurnEnd` (main) / `Hook.BeforeSideTurnEnd` (beta); damage modifiers,
  `Hook.ModifyDamage` and `CreatureCmd.Damage` take a `CardPlay` on the beta only; `CardPlay.Player` (beta) /
  `CardPlay.Card.Owner` (main); `CreatureCmd.LoseBlock` gives the remover on the beta only (main: the player whose card
  is being played); campfire leaving: the beta's "leave / skipped" messages, the main branch's `NRestSiteRoom._ExitTree`;
  cards / powers that only exist on the beta (Un pour tous, Âme sœur / Soulbound) are left out on the main branch.
- **1.2.0 (2026-11-13): beta v0.113.0, main v0.111.0.** The main branch moved to the former beta (v0.111.0) and the
  beta to v0.113.0. `GameBranch.IsBeta` (`CardPlay.Player`) is now true on both branches; `GameBranch.IsV113`
  (`Hook.ShouldTakeExtraTurn` has 3 parameters) tells them apart, and `GameBranch.Name` is "beta" (v0.113.0), "main"
  (v0.111.0) ("main-old", v0.107.1, until 1.2.2). v0.113.0 differences:
  `Hook.ShouldTakeExtraTurn` gives the model in an out parameter (`GameApi.ShouldTakeExtraTurn`); the damage funnel is
  `CreatureCmd.Damage(…, IEnumerable<DamageTargetData>, …, ModifyDamageHookType)` (`DamageCommandV113`); the kill funnel
  takes the card (`CreatureCmd.Kill(creatures, cardSource, force)`, `KillCommandV113`); fixed hit counts are in a
  "HitCount" variable instead of "Repeat" (`BlockAlternatives.HitCount` reads both).
  `lib/beta/` = v0.113.0, `lib/default/` = v0.111.0 (both decompiled in the same folder layout, for diffs).
  Game modes are read by name (`GameModes`, `GameMode.ToString()`): the enum values differ (main: Custom = 3; beta: Dread =
  3, Blitz = 4, custom runs = Standard + `IsCustomGame`, read in a `[BranchOnly]` method). The Fool's arrows:
  `Hook.AfterArrowShot` (beta only, `ArrowShot` patch, `Prepare()`), `ValueProp.FromArrow` read as 0x20 behind `IsV113`.
- **Build check:** PatchCheck runs on the same DLL with `lib/beta` and with `lib/default`: every active patch matches one
  method, every reflection target exists, and every game type / member the DLL names exists in that game build unless
  only `[BranchOnly]` methods use it. The build fails otherwise.
