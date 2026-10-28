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

## Safety rules
- Every patch is wrapped in try/catch and logs errors; the mod must **never** crash the game.
- The mod never modifies game state.
- If a patch target is missing (after a game update), that stat is disabled and a warning is
  logged; the rest of the mod keeps working.

## Game versions (beta + default)
- `lib/beta/` and `lib/default/` hold the game DLLs for each branch.
- The build produces one DLL per branch if their APIs differ; otherwise one shared DLL.
- `docs/COMPATIBILITY.md` tracks which game versions were tested.

## Risks to verify in the spike (Phase 1) — see GAME-INTERNALS.md
- R1: Does each client see damage events for **all** players (local simulation)?
- R2: How can we link a damage instance to its **source** (card, power, orb, relic)?
- R3: Can we read block **owner/giver** when block is absorbed, so we can compute "prevented for allies"?
- R4: Where do we hook the rewards and end-of-run screens?

## Game branches (v0.17.22)
The mod is built separately for each game branch: `./build.sh beta` (lib/beta, v0.111.0) and `./build.sh default`
(lib/default, main branch v0.107.1), output in `dist/<branch>/KikiMAIster/`. The csproj defines `GAME_BETA` or
`GAME_DEFAULT`; the few differences are under `#if`:
- turn end hook: `Hook.BeforeTurnEnd` (main) / `Hook.BeforeSideTurnEnd` (beta);
- damage modifiers and `Hook.ModifyDamage` take a `CardPlay` on the beta only; `CreatureCmd.Damage` likewise;
- `CardPlay.Player` (beta) / `CardPlay.Card.Owner` (main); `CreatureCmd.LoseBlock` gives the remover on the beta only
  (main: the player whose card is being played);
- campfire leaving: the beta's "leave / skipped" messages, the main branch's `NRestSiteRoom._ExitTree`;
- cards / powers that only exist on the beta (The Ball, Tutor, Soulbound) are matched by id or left out.
