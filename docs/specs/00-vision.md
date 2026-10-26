# 00 — Vision

**Status:** DRAFT — awaiting validation by Zaille75

## What KikiMAIster is
An all-in-one statistics mod for **Slay the Spire 2**, focused on **multiplayer (co-op)** runs.
It shows, for each player, what they contributed to the fight: damage, debuffs, protection
of allies, damage taken, and more. It also works in solo.

## Who it is for
- Zaille75 and the friends he plays with (first users).
- Any StS2 player: the mod is published publicly (Steam Workshop + Nexus Mods + GitHub).

## Goals
1. **Accurate:** every number has a precise written definition (see `01-metrics.md`).
2. **Three views:** a live overlay during combat, an end-of-combat summary and an end-of-run summary.
3. **Harmless:** read-only, so it never changes gameplay and never crashes the game. Only one
   player in the lobby needs to install it to see stats for everyone (to be confirmed in the
   technical spike).
4. **Extensible:** new stats (records, combat duration, potion use, …) can be added later
   without rewriting the mod.
5. **Follows the game:** works on the **beta** branch first, and on the default branch when possible.

## Non-goals (for now)
- Changing gameplay, balance or cards.
- Online leaderboards or sending data to a server.
- Replays.

## Success criteria for v1.0
- Zaille75 plays a full co-op run with the mod on the beta branch, with no crash.
- The numbers shown match a manual check on a few sample fights.
- The mod is published on the Steam Workshop and installable by friends in one click.
