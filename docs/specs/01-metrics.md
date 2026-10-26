# 01 — Metrics

**Status:** DRAFT — awaiting validation by Zaille75

Every stat is computed **per player**. A pet or summon (e.g. a minion) counts for its owner.
Each stat exists at three scopes: **turn**, **combat** and **run**.

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

## B. Debuffs applied ("negative effects")

| Stat | Definition | Tag |
|---|---|---|
| Debuffs applied | Count and total stacks of each debuff this player put on enemies (Vulnerable, Weak, Poison, …). | v1 |
| Damage from damage-over-time debuffs | Damage from DoT debuffs such as Poison, credited to whoever applied the stacks. If several players stacked it, the damage is split proportionally (see open question Q2). | v1 |

## C. Protection & support

| Stat | Definition | Tag |
|---|---|---|
| Block gained | Block this player gave **to themselves**. | v1 |
| Block given to allies | Block this player gave **to other players**. | v1 |
| Damage prevented — own block | Enemy damage actually absorbed by block this player created **on themselves**. | v1 |
| Damage prevented — for allies | Enemy damage actually absorbed by block this player gave **to an ally**. | v1 |
| Damage prevented — Weak & co. | Damage enemies did **not** deal because of this player's Weak (or similar) on them, i.e. what the attack would have dealt without the debuff minus what it dealt. | v2 |
| Healing | HP restored, to self and to allies, shown separately. | v1 |

## D. Damage taken

| Stat | Definition | Tag |
|---|---|---|
| HP lost | HP this player lost (from enemies, self-damage and other sources, shown separately). | v1 |
| Damage blocked | Incoming damage absorbed by this player's block, from any source. | v1 |
| Debuffs received | Debuffs and status cards (Wound, Dazed, …) this player received. | v1 |

## E. Later extensions (not in v1)
Records (best combat, fastest kill…), combat duration (turns and real time), potion use,
cards played, energy spent, gold, etc.

---

## Open questions — Zaille75 to answer
- **Q1.** Should "negative effects" also include **debuffs players put on allies or on themselves**
  (some cards do this)? Proposal: yes, shown separately as "friendly fire".
- **Q2.** Poison stacked by two players: split ticks **proportionally to stacks applied**
  (proposal), or credit everything to the **first/last** player to apply it?
- **Q3.** Is block that **expired without absorbing anything** interesting? Proposal: show it as
  "wasted block", low priority.
- **Q4.** Should enemy damage blocked by an ally's block count as "prevented for allies" for the
  ally who gave the block? Proposal: yes (that is the definition in section C).
