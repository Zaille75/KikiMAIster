# Game internals (findings from decompiled v0.111.0 beta)

## Multiplayer
Lockstep: players' actions (`PlayCardAction`, `UsePotionAction`…) are synchronized and **every client
simulates the whole combat**; `ChecksumTracker` compares states at turn boundaries. → One install sees
everyone's stats (R1 — to confirm in-game).

## Capture points
- `CombatHistory.Add(ICombatState, CombatHistoryEntry)` (private, sync): single choke point, only during
  live combat. Entries: `DamageReceivedEntry` (Result, Receiver, Dealer, CardSource), `BlockGainedEntry`
  (Amount, Receiver, CardPlay), `PowerReceivedEntry` (Power, Amount, Applier, Actor=target), `PotionUsedEntry`,
  `CardPlayStarted/Finished`, `EnergySpentEntry`, `OrbChanneledEntry`, `SummonedEntry`, `MonsterPerformedMoveEntry`…
- `DamageResult`: BlockedDamage, UnblockedDamage (excl. overkill), OverkillDamage, WasTargetKilled, Props.
- `Hook` static class (async): AfterCombatEnd, AfterCombatVictory, AfterBlockCleared, AfterSideTurnStart,
  AfterPotionUsed, AfterPowerAmountChanged… Harmony prefixes run synchronously at call time.
- `CreatureCmd.LoseBlock(ctx, target, amount, remover)` = block removed without damage. `CreatureCmd.Heal`.
- Official mod API `ModHelper.SubscribeForCombatStateHooks` (subscribe an AbstractModel) — not used yet.

## Attribution gaps (R2/R3)
- Block: `GainBlock` has no giver, only `CardPlay`. No card → relic/power/potion/orb. Ally-giving without a
  card: `BeaconOfHopePower`, `RampartPower`, potions (`BlockPotion`, `ShipInABottle`, `Fortifier`) → need
  context tracking (current potion user / current power owner).
- Poison: damage with `Dealer = null`, `CardSource = null`, `Unblockable|Unpowered`, from `PoisonPower.Trigger`
  → attribute via our own per-applier stack tracking.
- Creature: `IsPlayer`, `Player.NetId`, `IsPet`/`PetOwner`, `IsEnemy`, `Name` (player name for players).

## Mod loading
Manifest fields: id, name, author, description, version, has_pck, has_dll, dependencies [{id,min_version}],
affects_gameplay, min_game_version. DLL loaded with `LoadFromAssemblyPath`. Workshop items have a Steam
**branch-support** check (useful for beta vs default).

## In-game test results
- **v0.0.1, Necrobinder solo, 3 fights:** no crash. Block, cards and energy were logged. **No DamageReceived / PowerReceived
  entries at all**, even though CreatureAttacked entries were present. Cause unknown; v0.0.2 re-test needed on Necrobinder.
- **v0.0.2, Ironclad solo, 3 fights:** damage, Vulnerable, Strength and enemy debuffs captured at all four levels
  (Add, History.DamageReceived, History.PowerReceived, Hook.AfterPowerAmountChanged).
  - **The killing hit that ends the combat is missing** from DamageReceived: CreatureCmd skips the history
    when `CombatManager.IsEnding`. It is present in `CreatureAttackedEntry.DamageResults` → capture both and
    dedupe by DamageResult reference. A non-attack final kill (e.g. a Poison tick) still needs another source.
  - `Hook.AfterDamageReceived` is not called for kills → not usable as the main source.
  - Debuff expiry shows as PowerReceived with a negative amount and `Applier = null`.
  - `DamageResult.UnblockedDamage` excludes overkill (Volley: unblocked=5 overkill=10 killed=True).
- **v0.0.2, Necrobinder solo, 1 fight:** everything captured this time (damage, Poison, Doom, Osty).
  - Likely cause of the v0.0.1 gap: the JIT inlined the private `CombatHistory.Add` into the `DamageReceived` /
    `PowerReceived` wrappers, bypassing our patch. **Rule: patch the public wrappers (DamageReceived,
    PowerReceived, BlockGained, CreatureAttacked…), never the private `Add`.**
  - Poison tick: `Dealer = null`, `props = Unblockable, Unpowered` → attribute with our own stack tracking.
  - The **final Poison tick that kills the last enemy is missing** (IsEnding, and not an attack). Fallback
    needed: `Creature.LoseHpInternal` postfix while `CombatManager.IsEnding`, with dealer/card context
    taken from a `Hook.BeforeDamageReceived` prefix.
  - Power-triggered damage (e.g. SLEIGHT_OF_FLESH: "deal 9 when you apply a debuff"): dealer = player,
    `card = null`, `props = Unpowered` → source "power".
  - Osty (pet, named "Osty" here) takes enemy hits aimed at the Necrobinder and its damage is credited to the owner via `PetOwner`.
  - Doom: kills via `Hook.AfterDiedToDoom` → kill credit to the Doom applier.

## Multiplayer lobby
Peers exchange `PeerVersionInfo`: only mods with `affects_gameplay: true` must match. KikiMAIster
(`affects_gameplay: false`) goes into `otherMods` and should not block friends without it (confirm in co-op).
