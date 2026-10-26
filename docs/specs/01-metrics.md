# 01 — Metrics

**Status:** DRAFT v2 — Q1–Q3 validated, block attribution (section F) awaiting validation

Every stat is computed **per player**. A pet or summon (e.g. a minion) counts for its owner.
Each stat exists at three scopes: **turn**, **combat** and **run**.
All stats work the same in **solo** (one player) and in **co-op**.

Tags: **[v1]** = first release · **[v2]** = later release (needs harder attribution).

---

## A. Offense

| Stat | Definition | Tag |
|---|---|---|
| Damage dealt | HP actually removed from enemies by this player. Overkill is excluded. | v1 |
| Damage into block | Damage absorbed by enemy block. | v1 |
| Overkill | Damage beyond the HP the enemy had left. | v1 |
| Total raw damage | Damage dealt + damage into block + overkill. | v1 |
| Kills | Number of enemies this player landed the killing hit on. | v1 |
| Biggest hit | Largest single damage instance (combat and run records). | v1 |
| Damage by source | Breakdown by source: cards, powers (e.g. Poison, Thorns), orbs, relics, pets, potions. | v1 |
| Damage enabled for allies | Extra damage allies dealt **because** of this player's Vulnerable (or similar debuffs) on the target. | v2 |

## B. Debuffs ("negative effects")

| Stat | Definition | Tag |
|---|---|---|
| Debuffs applied | Count and total stacks of each debuff this player put on **enemies** (Vulnerable, Weak, Poison, …). | v1 |
| Damage from damage-over-time debuffs | Damage from DoT debuffs (Poison…) is credited to the players who applied the stacks, **split in proportion to the stacks each one applied** and still active. Example: A applies 6 Poison, B applies 4 → each tick is credited 60% to A and 40% to B. | v1 |
| Friendly fire | Debuffs and status cards (Wound, Dazed…) a player puts on **an ally or on themselves**, and damage a player deals to allies or themselves. Shown in its own section, never mixed with enemy stats. | v1 |

## C. Protection & support

| Stat | Definition | Tag |
|---|---|---|
| Block gained | Block this player gave **to themselves**. | v1 |
| Block given to allies | Block this player gave **to other players**. | v1 |
| Damage prevented — own block | Damage absorbed by this player's own block layers (see section F). | v1 |
| Damage prevented — for allies | Damage absorbed by block layers this player gave to an ally (see section F). | v1 |
| Wasted block | Block this player gave **themselves** that never absorbed anything (see section F). | v1 |
| Useless help | Block this player gave **an ally** that never absorbed anything (see section F). | v1 |
| Damage prevented — Weak & co. | Damage enemies did **not** deal because of this player's Weak (or similar) on them. | v2 |
| Healing | HP restored, to self and to allies, shown separately. | v1 |

## D. Damage taken

| Stat | Definition | Tag |
|---|---|---|
| HP lost | HP this player lost, split by source: enemies / friendly fire / self / other. | v1 |
| Damage blocked | Incoming damage absorbed by this player's block, whoever gave it. | v1 |
| Debuffs received | Debuffs and status cards this player received from enemies. | v1 |

## E. Later extensions (not in v1)
Records (best combat, fastest kill…), combat duration (turns and real time), potion use,
cards played, energy spent, gold, etc.

---

## F. Block attribution: "prevented", "wasted" and "useless help"

### Principle
Help counts only if it **actually saved HP**: the damage would have reached HP without it.

### Rule: block layers, consumed first-in, first-out (FIFO)
1. Each time a creature gains block, the mod records a **layer**: (giver, amount, time).
   Block a player gives themselves is their own layer; block from an ally is that ally's layer.
2. When a hit is absorbed by block, layers are consumed **from the oldest to the newest**.
   The absorbed amount is credited to each layer's giver as "damage prevented".
3. When block disappears without absorbing damage (block reset at turn start, block removed by
   an enemy effect…), the remaining layers become **wasted block** (own layers) or
   **useless help** (layers given by an ally).

**Why FIFO:** block the player **already had** is always used first. Help only counts beyond what
the player could already absorb, which is exactly your example. It is also easy to explain.

### Examples
| # | Situation | Result |
|---|---|---|
| 1 | B has 5 block. A gives B 5 (total 10). Enemy hits B for 5. | B: 5 prevented (own). A: 0 prevented, **5 useless help**. |
| 2 | B has 5 block. A gives B 5. Enemy hits B for 8. | B: 5 prevented. A: **3 prevented**, 2 useless help. |
| 3 | B has 0 block. A gives B 6. Enemy hits for 10. | A: 6 prevented. B loses 4 HP. |
| 4 | A gives B 5, **then** B plays Defend (5). Enemy hits for 5. | A: 5 prevented (oldest layer). B: 5 wasted. |
| 5 | A gives B 5, C gives B 5, B has 0. Enemy hits for 5. | A: 5 prevented (first). C: 5 useless help. |
| 6 | Multi-hit: 3 hits of 4 on B, who has own 5 + 5 from A. | Hit 1: B 4. Hit 2: B 1 + A 3. Hit 3: A 2, B loses 2 HP. Totals: B 5, A 5 prevented. |
| 7 | A gives the whole team 4 block (one card). | One layer per recipient, each attributed separately. |

### Edge cases
| Case | Rule |
|---|---|
| Block kept across turns (Barricade-like effects) | Layers keep their giver and order; they become wasted/useless only when they actually disappear. |
| Block removed by an enemy effect (not by damage) | Counts as wasted / useless help (it saved nothing). |
| Damage that ignores block (HP loss) | Consumes no layer. |
| Damage from any source absorbed by block (enemy, self-damage, friendly fire) | Counts as prevented: the block did save HP. Shown with its source in the detail. |
| Block **left when the combat ends** | **Not counted as wasted.** Defending on the last turn is normal, so it would only add noise. ⚠ To validate. |
| Block consumed or converted by a card instead of damage (e.g. a card that spends block) | Consumed FIFO, counted neither as prevented nor as wasted, but tracked as "block spent". |
| Block gain modified (Frail, Dexterity…) | The layer records the amount **actually gained**. |
| Block created by a relic, potion or power | The giver is the player who owns that relic, potion or power. |
| Block gained when the player dies/the combat ends in the same instant | Ignored. |

### Open questions — Zaille75 to answer
- **F1.** Is FIFO OK? The alternative "own block first, then allies' gifts" would make example 4
  give the credit to B instead of A.
- **F2.** Is it OK that block left at the end of combat is not counted as wasted?
