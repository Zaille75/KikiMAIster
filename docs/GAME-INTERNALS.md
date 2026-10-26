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
