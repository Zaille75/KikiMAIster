# 01 — Metrics

**Status:** VALIDATED (v3) — sections A–F validated by Zaille75; section E "lost opportunity" is a proposal

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
| Overkill avoidable (v0.17.42, Zaille75) | Column "Excédent" of Dégâts, shown like Inutile for block: overkill / of which **unavoidable**. Avoidable = overkill of a single-target attack the player (or their pet) aimed while another enemy was alive. Unavoidable = the rest: last enemy, attacks on all or random enemies, powers, relics, orbs, poison… Trophies: **Bulldozer** (most overkill, fun) and its mirror **Chirurgien** (least overkill, victory only), like Maçon du dimanche / Maître d'œuvre. | v0.17.42 |
| Total raw damage | Damage dealt + damage into block + overkill. | v1 |
| Kills | Number of enemies this player landed the killing hit on. **v0.17.38:** once per enemy and fight (an enemy that comes back, Reconnexion or Adaptable, is killed once). Minions dying with their leader credit nobody (validated by Zaille75). | v1 |
| Biggest hit | Largest single damage instance (combat and run records). | v1 |
| Damage by source | Breakdown by source: direct (the card played), powers (e.g. Thorns), damage over time (Poison…), orbs, relics, pets, potions. **The origin wins over the mechanism (v0.8.0):** a power or debuff applied by a potion (Thorns from Liquid Bronze, Demise from Powdered Demise) counts as potion damage, split by stacks with the same power from cards. A player's power that deals damage with no dealer (Haunt: damage when a Soul is played) counts for the player holding it. | v1 |
| Invulnerable phase | Damage to an enemy the game shows with infinite HP (Waterfall Giant about to explode) is not counted. Debuffs on it still are. | v1 |
| Damage enabled | Extra damage dealt **because** of this player's Vulnerable on the target (rule §B2), split into "for allies" (co-op section) and "for self". | v1 (v0.9.0) |

## B. Debuffs ("negative effects")

| Stat | Definition | Tag |
|---|---|---|
| Not a debuff (v0.17.1) | **À l'attaque !** (Sic 'Em) marks an enemy so that the Necrobinder's Osty summons when it hits it. The game calls it a debuff, but it does nothing against the enemy: it is not counted in Affaiblissements (nor in Maître des malus, whose records were cleared once). |
| Debuffs applied | Count and total stacks of each debuff this player put on **enemies** (Vulnerable, Weak, Poison, …). Shown as one column per debuff, "x/y" = times applied / total stacks. A buff applied with a negative amount (Strength down, Dexterity down) counts as a debuff, shown as "Force −". The overview has one column per effect: a temporary loss (Enfeebling Touch…) is counted in "Force −", whatever card caused it; the details list each origin. | v1 |
| Damage from damage-over-time debuffs | Damage from DoT debuffs (Poison…) is credited to the players who applied the stacks, **split in proportion to the stacks each one applied** and still active. Example: A applies 6 Poison, B applies 4 → each tick is credited 60% to A and 40% to B. | v1 |
| Doom kills | When Doom kills an enemy, the HP it removes counts as damage-over-time for the Doom appliers (split by stacks) and the kill goes to the biggest share. | v1 |
| Friendly fire | Debuffs and status cards (Wound, Dazed…) a player puts on **an ally or on themselves**, and damage a player deals to allies or themselves. v0.9.0: no own section any more: shown in the co-op section (dealt to allies) and in "Taken" (received from allies). | v1 |

### B2. Who gets credit for a debuff's effect (v0.9.0, validated by Zaille75)
- The game reports, for every real hit, which powers changed its damage. The mod measures what each debuff changed:
  - **Vulnerable** on an enemy: extra damage = damage − damage ÷ multiplier (×1.5, or more with relics).
  - **Weak** on an attacking enemy: damage avoided = damage ÷ multiplier − damage.
  - **Strength down** on an attacking enemy: the Strength it lost (per hit).
- **Duration debuffs (Vulnerable, Weak): first in, first out, turn by turn.** Each application is queued; each turn lost
  removes the oldest turn. Example: turn 1, A applies 2 Vulnerable; turn 2, B applies 2 → the turns 1–2 of Vulnerable
  count for A, the turns 3–4 for B.
- **Strength down** does not decay by turns: the loss is shared by the stacks each player removed. **v0.17.38:** the
  loss counted on a hit is the Strength players removed that is still gone (permanent losses + temporary ones still on the
  enemy), whatever Strength the enemy has itself (an enemy at +3 with Rituel and −2 from a player: 2 avoided per hit, before
  only a negative total counted), × the hit's Weak and the target's Vulnerable.
- **Effects that use all the Vulnerable stacks at once (v0.11.0, validated by Zaille75):** shared by the turns each
  player still has in the queue (the proportion rule), not by the current turn.
  - **Bully (Harcèlement):** its "+2 per Vulnerable stack" part of the damage (measured before the ×1.5).
    Example: A applied 2 Vulnerable, B 1 → Bully gets +6: 4 enabled by A, 2 by B.
  - **Dominate (Domination):** the Strength it gives remembers who applied the target's Vulnerable (same proportion,
    including the player's own new stack). For the rest of the combat, on each of that player's attacks, the damage
    added by those Strength points (Strength × Vulnerable multiplier) is enabled by them. If the player has other
    Strength, only the borrowed share counts; Strength lost later reduces it.
- **Co-op card audit (v0.12.0): help from an ally counts for that ally** (all 597 cards checked; same principle):
  - **Powers that remember their applier** and change a hit: debuffs on the enemy that boost allies' attacks
    (**Flanking**, **Knockdown**), buffs an ally gave the attacker (**One for All**), and any future power of that kind.
    The extra damage (or damage avoided, for a debuff on an attacking enemy) is credited to the applier directly.
  - **Strength given by an ally:** permanent (**Blaze**) — those points count for the giver for the rest of the combat,
    like Dominate; temporary (**Coordinate**) — read at each hit from the temporary power, which keeps its applier.
  - **Concoct (v0.13.0, corrected):** the Poison the ally applies when they hit is theirs (its damage counts for them,
    the attacker); its damage is also "enabled" by the Concoct player.
  - **Cards created for an ally** (Blade Symphony's Shivs, Largesse, Glimpse Beyond, Outrage's copies…): their damage is
    enabled for the creator.
  - **Tag Team:** the damage of the extra plays it gives an ally's attack is enabled for the Tag Team player.
  - **Strangle** (one instance per player, triggered by its own player's cards): each instance's damage counts only for
    the player who applied it.
  - **Growing cards** (never wasted as block / weak attack): Genetic Algorithm, The Scythe, The Ball, Claw, Maul,
    Rampage. (Kingly Punch grows when drawn, not played: not in the list.) **Missed (v0.13.0, counted in Gâché since v0.13.1):** once per card and fight,
    when the card could have been played at the end of at least one turn, was never played, and is still in the deck
    (hand, draw or discard pile) when the fight ends.
  - Already right, no change: Oblivion (Doom credited to its applier), Magic Bomb and Constrict (by stacks), Sic 'Em,
    Legion of Bone (pet HP), Rally / Lift / Demonic Shield (block given), Intercept / Covered (block).
- **General rule (confirmed by Zaille75, v0.13.0): every damage, base and extra, counts for the attacker** (Dégâts); the
  extra part caused by another player's effect is tracked in addition as "Permis" for that player (co-op section).
- **The damage itself always counts in full for the attacker** (Dégâts). "Damage enabled" is an extra credit shown in
  the co-op section ("Permis") for allies, and in the debuff details for self; nothing is taken from the attacker.
- Stacks an enemy put on itself credit nobody.
- Extra damage from a player's Vulnerable is "damage enabled" (for self or for allies). Damage avoided thanks to a
  player's Weak / Strength down is "prevented" (for self or for allies) and is part of "Évités".
- **Only HP really dealt (v0.17.19):** extra damage from a debuff or an ally's help (Vulnérable, Pendaison, Strength
  borrowed…) counts only for its share of the HP the hit removed (HP ÷ raw damage), like "Dégâts": not the part that
  went into block or beyond the enemy's HP. Example: Pendaison ×4 on a 40 hit that removed 5 HP (4 into block, 31
  overkill) used to credit 30, now 3.75.
- **Only HP really saved (v0.17.3, Zaille75's example):** damage avoided by a debuff (Weak, Strength down, Tank's
  Guarded…) counts only for the part that would have gone through the block without it. Example: Weak turns a 6 hit into
  4 on a player with 5 block: without Weak, 1 would have gone through → 1 saved, not 2. Weak's amount uses the game's
  rounding (6 × 0.75 = 4.5 hits for 4, so Weak avoided 2).
- **Pet HP saved (v0.17.4, Zaille75):** when that hit was on a pet (Osty), the HP saved are the pet's, not the player's.
  They are kept aside and count only if the pet **dies later in the same fight** and its owner then **loses HP to
  enemies**: up to the HP the owner loses (without the saved HP, the pet would have died sooner and the owner would
  have taken that much more). Otherwise they never count.
- **Attacks that grow with the pet's HP (v0.17.5, Zaille75):** Déchaînement (base + 1 per **current** HP of Osty) and
  Protecteur (base + 1 per **max** HP). The part of their damage due to the HP (damage × HP part ÷ whole, so Strength,
  Vulnerable… are shared in proportion) is "Permis" for the allies behind those HP: HP an ally's debuffs saved on the pet
  during its current life (current HP only), then the rest by who gave the pet its HP (summon, heal, max HP: same
  proportions as the damage the pet absorbs, rule §D2). The owner's own share credits nobody.
- **Block from the pet's HP (v0.17.6, Zaille75):** Sacrifice gives 3 × Osty's **max** HP as block. The allies who gave
  Osty HP enabled their share of that block (same proportions): Coop column **"Armure permise"**, and what that block
  then stops counts in their "PV sauvés" (the owner keeps the full block in his own protection, like Permis for
  damage). HP saved by debuffs are current HP: they do not change max HP, so they never change Sacrifice's block.
- **Block on a hit aimed at the pet (v0.17.8, Zaille75, to be judged in game):** pets use their owner's block, but that
  block saved the **pet's** HP. It now follows the same rule as debuffs: it counts (as the giver's block protection:
  own "Armure", or "PV sauvés" for an ally's block) only once the pet died and its owner then loses HP to enemies in
  the same fight, and while the pet lives it feeds Déchaînement's Permis for an ally who gave that block. "Bloqués"
  (damage blocked) stays immediate. The previous rule (block on the pet counted at once) is kept in git as the tag
  `backup/v0.17.7-pet-protection-immediate`.

## C. Protection & support

| Stat | Definition | Tag |
|---|---|---|
| Block gained | Block this player gave **to themselves**. | v1 |
| Block given to allies | Block this player gave **to other players**. | v1 |
| Damage prevented — own block | Damage absorbed by this player's own block layers (see section F). | v1 |
| Damage prevented — for allies | Damage absorbed by block layers this player gave to an ally (see section F). | v1 |
| Wasted block ("gâché") | Block this player gave (to themselves or an ally) that never absorbed anything, **while a real alternative existed**: a playable attack stayed in hand (rule in section F). | v1 |
| Useless block ("inutile") | Block that never absorbed anything **with no alternative**: no playable attack in hand, or block from a relic, power, potion or auto-played card. | v1 |
| …of which unavoidable (v0.17.34) | Part of the useless block the player could not avoid: the leftover of a block that stopped part of the damage (needed 1 more, only a 5-block card in hand: 4 unavoidable), or block no card chose (relics, powers, orbs). Shown as "Inutile 29/12". | v1 |
| Heals after a fight (v0.17.38) | Heals right after a won fight (Sang ardent, Sang noir, Os à viande, a potion on the reward screen) count in "Soins hors combat", kind "après combat" (the game's history puts them on the fight's floor, which was left out). The Ancients' heal at the start of each act is shown apart ("Anciens"); Neow's is left out (the game sets HP to 0, then heals to full). | v1 |
| HP lost outside fights (v0.17.34) | HP lost on floors without a fight (events…), from the game's map history (damage taken per floor); Run tab, Dégâts "Hors combat". **v0.17.35:** HP healed outside fights too ("Soins hors combat": campfire rest, events, relics…); both split in the details by kind of floor (events, campfires, others). An event that turns into a fight counts as a fight; **v0.17.36:** what that event did to HP before its fight (heal 22 then fight…) is read when the fight starts (HP then minus HP at the end of the previous floor, once per floor) and added to the events' part. | v1 |
| Gold and curses (v0.17.36) | Over the run, from the game's map history (every floor: fights, events, shops…): gold gained, spent, lost (lost + stolen), held now; most gold held and purchases (relics, potions, colorless cards) in the details; curses added to the deck (Fléau de l'ascendant left out: every player gets it from the ascension level) and removed. Section "Or et malédictions" (Run tab). Trophies: **Chercheur d'or** (most gold gained), fun **Panier percé** (most spent), **Radin** (most gold held at the end of the run), **Chat noir** (most curses received). | v1 |
| …of which given to allies | Part of the wasted + useless block that was given to allies (co-op detail). | v1 |
| Damage prevented — relics & powers | HP loss reduced by a relic or power on the player or their pet (Tungsten Rod, Intangible, Buffer…), per source. Included in the "Prevented" total. If several reduce the same hit, the saving is shared equally. **v0.17.38:** Intangible and Dur à cuire cap the hit before the block: what the hit would have done past the block without the cap, minus what it did, is counted (before, Intangible was never counted). La botte (raises a loss) never shares a saving. On a capped hit, Vulnerable and Strength add nothing. | v1 |
| Damage prevented — Weak & co. | Damage enemies did **not** deal because of this player's Weak or Strength down on them (rule §B2), for self or allies. | v1 (v0.9.0) |
| Healing | HP restored, to self and to allies, shown separately. | v1 |

## D. Damage taken

| Stat | Definition | Tag |
|---|---|---|
| HP lost | HP this player lost, split by source: enemies / friendly fire / self / other. **v0.17.38 (Zaille75):** a status or curse card hurting its holder (Brûlure, Déclin, Toxicité, Infection, Érosion, Regrets, Malchance, Appel) counts for whoever put it in the deck: an enemy → enemies, an ally → friendly fire, the player (their own card, or a curse of their deck) → self-inflicted. Damage with no dealer from an enemy's power (Galvanisé) → enemies; from the player's own relic (Forces précaires, Champi aromatique, Poison royal) → self-inflicted. Status cards are never "cards given". **v0.17.7:** "Auto-infligés" column (HP lost to one's own cards and powers); debuffs a player puts on themself (Influx neuronal's Fatalité, boss-forced curses) are listed there, not in Coop's friendly fire. Damage over time on a player (Fatalité, an enemy's Poison) counts for whoever applied it (self / ally / enemy). | v1 |
| Damage absorbed by block vs pet | "Protection" shows the damage stopped by block and the damage absorbed by the pet in separate columns (v0.9.0). | v1 |
| Damage blocked | Incoming damage absorbed by this player's block, whoever gave it. | v1 |
| Debuffs received | Debuffs and status cards this player received from enemies. | v1 |

## D2. Pets & summons (e.g. Osty, the Necrobinder's summon)
| Stat | Definition | Tag |
|---|---|---|
| Damage dealt by pet | Counted in the owner's damage, and shown as source "pet" in the detail. | v1 |
| Damage absorbed by pet | Enemy damage the pet took instead of its owner. Counted as **protection** for the owner (it saved HP). | v1 |
| Pet deaths | Number of times the pet died. **v0.17.38:** the owner killing their own pet (Sacrifice) is a pet death only: the pet's HP stopped nothing (no "Familier" protection, no PV sauvés for allies who gave HP) and what it had saved is lost with it. | v1 |
| Pet HP given | HP given to the pet: summon, revive, max HP gain, heal (a heal that is part of a summon counts once). Credited to the player who gave it. Separate from block: **never wasted or useless**. A block card that also summons has "another effect" (F4), so its unused block is never wasted either. Not part of "Prevented" (the damage the pet then absorbs already is). | v1 |

**Q5 — validated:** damage absorbed by the pet counts as protection for its owner, not as damage taken.

## C2. Co-op (v0.9.0)
| Stat | Definition |
|---|---|
| Block given | Block this player gave to other players. |
| HP saved for allies | Damage stopped by block given to allies + damage avoided by allies thanks to this player's Weak / Strength down + healing given to allies. |
| Damage enabled for allies | Extra damage allies dealt thanks to this player's Vulnerable (§B2). |
| Friendly fire | Damage dealt to allies (details: debuffs and status cards put on allies or self). |
| Deaths | Times this player died. |
| Campfire heal (v0.17.17) | Réparer (Mend) at a campfire: the HP it heals on the ally count as healing given, so in that ally's "PV sauvés" and in Ange gardien (Partie tab). |
| Took for allies (v0.13.0) | Damage the player took instead of allies (Intercept: the extra share of each hit, 1 − 1/multiplier). |
| Energy given (v0.13.0) | Energy given to other players (Believe in You, Energy Surge…). |
| Cards and energy for next turn (v0.17.1) | A power that gives an ally cards or energy at the start of their next turn (Esquisse: Draw Cards Next Turn; Energy Next Turn) counts as cards / energy given when that next turn comes (**v0.17.2:** not if the fight ends first or the ally is dead). Souls that **Âme sœur** copies into the ally's draw pile are cards given by the Âme sœur player (their damage is "Permis" for them). |
| Block enabled by Dexterity (v0.17.38, Zaille75) | Oubli gives an ally temporary Dexterity: the block it adds to that ally's card block (within the Dexterity they really have) is "Armure permise" for the giver, and what it stops is their "PV sauvés", like Sacrifice's block. |
| Cards given (v0.13.0) | Cards drawn for, created for, or brought back to the hand of other players (Huddle Up, Plot, Blade Symphony, Largesse, Glimpse Beyond, Outrage, Tutor…). Orbs channeled for allies (Ignition) are in the details. |
| Most cards in a turn (v0.13.0) | The most cards this player played in one turn (trophy "Roi du combo"). |
| Tank (v0.15.0) | Tank makes its holder take 50% more attack damage and its allies (Guarded) 50% less. The damage an ally did not take counts as HP saved for that ally by the Tank's player (Ange gardien, "PV sauvés"); the extra damage the Tank's player takes while an ally is alive counts as "Encaissés" (taken for allies), like Intercept. |
| Pet given to an ally (v0.15.0) | A pet's HP are shared by who gave them (summon, max HP, heal; Légion d'ossements summons for every player). Damage the pet absorbs is split in the same proportions: the part from HP an ally gave counts as HP saved for that ally by the giver (Ange gardien), the rest as the owner's own "Familier" protection. A dead pet starts from zero. |
| Fortress vs guardian angel (v0.15.0) | Forteresse (trophy) = damage prevented **for yourself** (own block on you, your pet, relics/powers, your Weak / Strength down on enemies attacking you). What you prevent for allies is Ange gardien. Solo: unchanged. |
| Ally HP you could have saved (v0.15.0) | A playable block card that gives block to an ally (Lift, Rally, Constellation…; any ally / all allies) stayed in hand at the end of the turn with an ally alive, then allies lost HP to enemies before the next round: min(HP lost, its block) — for one ally the most any one ally lost, for all allies the sum — counts in "Évitables" (kind "allyBlockKept") and in the Traître trophy. |
| Best card play (v0.14.0) | The most damage dealt by one card play: every hit of the card, plus the cards it plays itself (Knife Trap's Shivs…), block and overkill included (trophy "Carte maîtresse"). |

## C3. Other stats (v0.9.0)
Cards drawn, played, exhausted, discarded; potions used.

## C5. Details and fight length (v0.17.39, Zaille75)
| Stat | Definition |
|---|---|
| Enchantment damage | The part of a card's hit added by its enchantment (Tranchant, Tezcatara's ember…): the game's damage without the enchantment, through the same modifiers, against the real hit, for its share of the HP removed. Already in the card's damage: a detail of Dégâts, no column. |
| HP lost by kind | HP lost split into enemy attacks, enemy reactions (an enemy's power: Thorns…), status and curse cards, debuffs (Poison, Doom…), self, allies, other. A detail of Dégâts, no column. |
| Heals by source | Heals a player gave (self and allies) by what healed: relic, card, potion, power. A detail of Protection. |
| Fight length | Rounds and real time of each fight (from its start to its end — **v0.17.42, Zaille75: a won fight ends when the last enemy dies**, not after the last animations; menus and pauses included; a fight reloaded after Save & Quit starts again). Run scope: number of fights, total rounds and time, average time. Details of "Cartes et potions"; the combat summary shows the time next to the rounds. |
| Fight records | Across runs, for each encounter won: fewest rounds and shortest time, kept apart by ascension level, number of players and, in solo, character (file `KikiMAIster.fights.cfg`). The first win of an encounter only sets the record; beating it shows "★ Nouveau record pour ce combat ! 4 tours (avant 5) · 2:10 (avant 2:45)" in the combat summary. Defeats never count. |

## C4. Run stats outside combat (v0.17.0)
| Stat | Definition |
|---|---|
| Max HP | Max HP gained and lost during the run (events, relics, cards…), per player. Read from the game's own map history, so it is complete after Save & Quit or a crash and never counted twice. Shown as the net change ("PV max" column of Dégâts, Run tab); trophies **Culturiste** (most gained) and **Peau sur les os** (most lost, victory only). |
| Campfire choices | How many times each player chose each campfire option (Rest, Smith, Mend, Lift, Dig…), read from the game's map history. Section "Feux de camp" (Run tab). |
| Chifumi | When several players pick the same treasure relic, the game settles it with rock-paper-scissors (random moves). The winner beats every other player of that fight. Per run: fights won / fought (Coop section). Across runs: for every pair of players, wins of each against the other (trophies window), saved in `KikiMAIster.chifumi.cfg`. A reloaded treasure room is not counted twice. |

## E0. Missed opportunities (v0.9.0)
| Stat | Definition |
|---|---|
| Missed lethal | **v0.17.38:** enemies whose death does not end the fight (Adaptable, Reconnexion, Illusion, Infesté, Stock, Éruption de vapeur, Surprise) are left out; Paresse limits the cards of the combination; a Résonnant card is left out, only one Lié card under Entraves. Turns where the player's **front hand** (the hand and energy at the start of their turn) could have killed every enemy that must die, yet **one of those enemies** was still alive at the end of the turn (v0.17.3: enemies that appear when one dies, like Parasite Krapo's larvae, do not count). Only attacks on one enemy or on all enemies, with the game's own damage computation (Strength, Vulnerable…) × hits; X-cost cards and cards whose damage is computed differently are left out. It is a **minimum**: draw, random effects and combos are ignored. |
| Cards played again (v0.17.38) | A card played again by Déflagration, Duplication, Forme résonnante, Droite-gauche, Amplification du signal or Relais is one choice: its copies add their hits and block to it, the energy and Stars are counted once, it is judged once (weak attack, gâché…) and counts once in cards played and Roi du combo. |
| Incoming attack (v0.17.38) | The damage a player will take before their block is cleared, used by every "block needed / covered" rule: the enemies' announced attacks computed for **that** player (the game's intent shows the local player's numbers: Tank / Abrité / Vulnérable made co-op figures wrong and different on each PC), plus blockable status cards in hand that hurt at the end of the turn (Brûlure…). It is 0 when the player takes an extra turn first (Ambre gris, Œil de Paël): no attack comes before that block is cleared. A card limit (Paresse, Résonnant) does not stop a card from being the alternative of a wasted block. |
| Gâché (v0.13.1: a count of misplays, like the other columns) | One per block card play whose block was wasted (rule F3). The block amount stays in the details. (Growing cards never played moved to **Choix** in v0.17.7.) |
| Free card not played (v0.17.26, in Gâché) | A useful card costing nothing (0 energy, no Stars: Neutralisation…) left in hand at the end of the turn counts once per card, unless kept on purpose (Retain, Stars, Exhaust — v0.17.34, Zaille75: an Exhaust card is never a mistake when not played, Surins and Entrave des ténèbres included, except for missed lethal; the v0.17.27 Shiv rule is dropped), **Rage** when no attack was played while it was in hand (v0.17.34: it would have given nothing), punished (Thorns the block cannot afford, Personal Hive), dealing no damage, or with no enemy left to hit. A free card is no longer the alternative of a wasted block (it is its own miss). |
| Wasted block | The "gâché" block of rule F3 (block that saved nothing while a useful card stayed in hand), with its explanation: incoming attack → block already there → card played → what could have been played. In "Protection", gâché and inutile are now one "Inutile" column. |
| Unused energy (v0.10.0) | Energy left at the end of the turn while a playable card that energy could pay was in hand. Not counted when the energy is kept (Ice Cream…) or wanted (Pael's Tears gives energy for leftover energy). **v0.17.1:** a block skill does not count when the player's block already covers the incoming attack (that block was not needed). **v0.17.18:** an Exhaust card (Feu follet…) does not count either: it is gone once played, keeping it is a choice. **v0.17.34:** an attack into Thorns counts only if the block covers the incoming attack **plus** the Thorns damage; nothing counts while an enemy is asleep (Matriarche lagavulin: not waking it may be on purpose). An Exhaust block card kept in hand is never a kept-block miss either. |
| Explanations kept (v0.17.17) | The latest explanations of every kind (missed lethal, wasted block, weak attacks, other misses: 40 of each per player) are saved with the run, so they survive Save & Quit and a restart. |
| Spare energy (v0.17.29, not a miss) | Energy lost at the end of the turn with nothing useful to spend it on: no affordable card, or only block cards when the block already covers the incoming attack, or only attacks the enemy punishes, or nothing left to hit. "Énergie en trop" column (Cartes et potions) and trophy "Batterie pleine". |
| Energy for nothing (v0.17.37) | Energy paid for a card that did nothing useful: a block card with no other effect whose block was not needed (block at the end of the turn minus its block still covers the incoming attack; **v0.18.4:** the block cards are judged from the last played back, each without the block of those already found useless — Armement 5 + Défense+ 8 + Défense 5 against 10 is 1 energy for nothing, not 2), or a card whose only effect was a useless debuff (F8) — same test as Pael's Tears. "Énergie pour rien" column (Potions et énergie, v0.18.4); added to "Énergie en trop" for the trophy "Batterie pleine". It can also be a miss (Gâché) when a useful card was left in hand. |
| Avoidable HP (v0.10.0) | A playable block card (affordable with the energy left) stayed in hand at the end of the turn, then the player lost HP to enemies before the next round: min(HP lost, that card's block) counts. |
| La grande manœuvre (v0.17.23, Zaille75) | Its 50 block comes with a power that kills the player as soon as an attack gets through. Kept in hand, it counts in "Évitables" only if HP were lost that round **and none in the next round** (playing it would then have been safe); if the fight ended before the next round, it counts. It is never the "bigger block" of a smaller block, nor a card worth playing for a relic. |
| Potions left (v0.10.0) | Potions usable in combat still held when the player died. |
| Smaller block (v0.10.0) | A block card played while a block card giving more block (same cost or cheaper) stayed in hand until the end of the turn, and the player's block was below the incoming attack. Not counted if the played card did something else. |
| Pen Nib (v0.10.0) | The attack Pen Nib doubled was weaker than another attack in hand the player could afford at that moment (same rules as weak attack, any affordable cost). |
| Orichalcum (v0.10.0) | The player ended the turn with block ≤ Orichalcum's block (6), from block cards played that turn with no other effect: without them, Orichalcum would have given more. |
| Pael's Tears (v0.10.2) | The player has Pael's Tears (energy next turn when energy is left) and ended the turn with 0 energy, after spending energy at any time that turn on a useless card (not only the last one): a block card with no other effect whose block was not needed (block at the end of the turn minus that card's block still covers the incoming attack), or a card whose only effect was a useless debuff (F8). Counted in the Relics column. |
| Useless debuff (F8, v0.10.4) | Not counted as "another effect" (rule F4), because it wears off having done nothing: **1 Weak** on an enemy that had none and does not attack this turn, or **1 Vulnerable** on an enemy that had none and that nobody hits with an attack before the end of the turn. Adding turns to a debuff already there is always an effect. A block card that only does that can be wasted, an attack that only does that can be a weak attack, and for Pael's Tears it is a useless card. |
| Growing card not played (v0.10.0; v0.17.7: in Choix; **v0.17.38: it was never counted**, the list was emptied each turn) | A growing card (Genetic Algorithm, The Scythe…) never played in the fight, still in the deck at its end, although at the end of at least one turn it was in hand and could have been paid with the energy left **or with the energy spent on a useless card** that turn (block not needed, useless debuff: same test as Pael's Tears). |
| Relic trigger missed (v0.10.0) | One more card would have triggered a relic this turn and a playable one was in hand: Shuriken / Kunai / Ornamental Fan / Kusarigama (3rd attack), Letter Opener (3rd skill), Rainbow Ring (the missing card type), Brilliant Scarf (the free Nth card, energy not needed), Screaming Flagon (the whole hand could have been played). **v0.10.1 — only when the extra card was worth playing:** the energy it needs would have been lost anyway (not kept by Ice Cream & co., not wanted by Pael's Tears; Brilliant Scarf's free card needs none); an attack must deal damage and not cost Thorns damage the block could not afford (rule F7); and when the player's block was below the incoming attack, the card must not take the energy a block card in hand needed (keeping energy for block is not a miss). Relic audit: the other 290 relics either do not depend on the cards chosen during the turn, count across turns (Nunchaku, Tuning Fork…), or reward choices the mod cannot judge (Art of War, Ripple Basin, Pocketwatch, Ivory Tile…). |
| Number of hits (v0.13.4, audit of every attack) | Damage estimates use the game's own hit count: the "Repeat" value (except where it counts something else: Javelot de glace = Frost orbs channeled, Marteau de famille = copies; v0.17.32), the game's computed hits ("CalculatedHits": En miettes = 1 + times the player lost HP this fight, Barrage…), 2 for the cards that always hit twice, 1–2 for Dismantle (Vulnerable). Cards whose hits depend on something the mod does not compute (Whirlwind, Fiend Fire, X-cost…) are never compared for weak attacks; for missed lethal they count 1 hit (minimum). |
| Wrong order (v0.14.0) | A card or potion that boosts later plays was used **after** a play it could have boosted, in the same turn, although it was already in hand (a potion: in the belt) when that play was made: **Vulnerable** put on an enemy that had none (the attacks that hit that enemy earlier lose 50% of their damage), **Strength** gained (each earlier attack hit loses that much), **Dexterity** gained (each earlier block gain loses that much), **Focus** gained (v0.17.32, Potion de focalisation…: each orb triggered by an earlier play loses that much — Lightning and Glass: per enemy hit, Frost: per block gain, Dark: per charge, damage it stores ("orderDark", counted even if not evoked yet); a Dark evoke is not counted, it deals what it stored before; Plasma gives energy, which Focus does not change). The earlier play must have been a choice (not auto-played) with no effect besides damage / block. Damage is only lost on enemies still alive at the end of the turn (capped at their HP + block); block is only lost when the block at the end of the turn was below the incoming attack (capped at the difference). Counted once per (earlier play, boosting card) in "Choix", with the damage or block lost in the details. |
| Invulnerable phase (v0.14.0) | An enemy the game shows with infinite HP (Waterfall Giant about to explode) is not an enemy left to kill: missed lethal ignores it, and when it is the only enemy left, unused energy and relic triggers are not counted that turn. |
| Cards kept on purpose (v0.17.0, rule validated by Zaille75) | Not playing a card is never a misplay when the player may keep it on purpose: a card that is **retained** (Retain, or the whole hand is kept: Main retenue, Runic Pyramid-like effects), or a card with a **Star cost** (Stars are not refilled each turn). Such a card is never the alternative of a wasted block, the "stronger attack" of a weak attack, the "bigger block" of a smaller block, a card that unused energy could have paid, or a card that would have triggered a relic. Missed lethal still uses them (winning the fight is always worth it), but only when the player has enough Stars. |
| Strength drop (v0.17.15) | A card the player chose that lowers their own Strength (**Amitié**: −2 Strength, +1 energy) was played **before** attacks that were already in hand: each of their hits by the player lost that Strength (playing the attacks first would have dealt more). Same rules as wrong order (enemies still alive at the end of the turn, capped at their HP + block). Osty's hits are not counted (Strength does not change them). Counted in "Choix": "Amitié avant Frappe : 4 dégâts perdus". |
| Potion after attacks (v0.17.30) | A damage potion killed an enemy that the player's attacks had already hit this turn, and the potion alone would have killed it before those attacks (its damage ≥ the enemy's HP + block then): each of those attacks (that did nothing besides damage) was wasted on it. One miss per attack in "Choix", with the damage it dealt there. |
| Slow (v0.17.0) | Slow (Bygone Effigy): each card played this turn makes that enemy take 10% more attack damage. A **non-attack card** played after an attack that hit the Slowed enemy, while it was already in hand when the attack was played, is a wrong order: that attack lost 10% of its base damage on that enemy (raw ÷ (1 + 0.1 × cards before) × 0.1). Same rules as wrong order (the attack did nothing besides damage, the enemy is still alive at the end of the turn). Counted in "Choix" (kind "orderSlow"). |
| Miniature Tent (v0.17.14, in Choix) | With Tente miniature every campfire option stays available after the first choice. Leaving the campfire (after a first choice) while a **useful** option was left counts once in Choix. Useless options: those the game greys out (Smith with nothing to upgrade, Cook…), Rest at full HP unless a relic rewards resting itself (Minuscule boîte aux lettres, Attrape-rêves, Oreiller royal…), Mend when every ally is at full HP. |
| Rest before the boss (v0.17.32, in Choix) | The player chose **Rest** at the campfire just before the boss (the floor before it), won the boss fight alive, and the HP they had **before resting** were more than the HP the boss fight cost (HP at its start − HP at its end): resting was not needed, another option (Smith…) was the better choice. Not counted with Tente miniature (the other options stay) or a relic rewarding the rest itself. **v0.17.34:** with the **Pantographe** (heals 25 at the boss fight's start), both sides count that heal (capped at Max HP): HP without resting + heal against the fight's cost from its HP after the heal. "Repos avant le boss : 40 PV avant de dormir, le combat en a coûté 12". |
| Stars spent (v0.17.34, Zaille75) | Stars are not refilled each turn. A card paid with Stars that only blocked or only dealt damage: its block was **not needed** (block at the end of the turn minus its block still covers the incoming attack) → **Gâché** ("Manteau d'étoiles+ : 3 étoiles dépensées, son armure ne servait à rien"), unless it already counts as a wasted block; or an **energy card left in hand, paid with the energy left**, did the same job (a block card covering what was missing; an attack dealing at least the damage it dealt to its target, no more Thorns) → **Choix** ("… : 2 étoiles dépensées, Défense faisait pareil avec l'énergie restante"). |
| Carapace endurcie (v0.17.34, Zaille75) | An attack whose every hit met an enemy whose Carapace endurcie (HP lost per turn limited) was already reached (0 damage), that did nothing else, counts in **Choix** only if something more useful was playable instead with the same energy and still in hand at the end of the turn: a block card when the block did not cover the attack, an attack that can hit an enemy without that limit reached, or another useful card. The hit that reaches the limit counts normally in the damage stats (before v0.17.34 it was dropped like the Waterfall Giant's infinite phase). |
| Overkill misplay (v0.17.42, Zaille75, in **Gâché**) | A single-target attack that did nothing but damage killed its target with damage to spare, while: (a) **"overkill"**: a smaller attack that was in hand and would also have killed it (estimate ≥ the target's HP + block before the big one) was played later that turn on another enemy, still alive at the end of the turn: swapped, that enemy would have taken (big attack's estimate on it − the small attack's damage) more, capped by what it had left; or (b) **"overkillFollowUp"**: what the card triggered right after (Trou noir after Stars were spent: any of the player's powers hitting enemies before the next card) would have killed the target alone (its smallest hit ≥ the target's HP + block before the card); aimed at another enemy alive at the end of the turn, the card would have dealt its damage there (capped by what that enemy had left). Counted when at least 1 damage is lost. |
| Slow, attacks in the wrong order (v0.17.42, Zaille75, in Choix) | Against Slow, attacks should go from the smallest to the biggest. A bigger attack (base damage, Slow removed) played before a smaller one that was already in hand — the player's own, or **an ally's in co-op** — lost 10% × (cards played between them) × (its base − the smaller one's base) on that enemy (kind "orderSlow", same rules as wrong order). Counted for the player of the bigger attack, only when at least 1 damage is lost (8 then 6 one card apart = 0.2: not counted). |
| Max HP after a capped heal (v0.17.42, Zaille75, column **Hors combat** of Occasions manquées) | Outside fights, a heal cut by the Max HP (healing more than missing), then Max HP gained on the same floor (Faux marchand: Fiole de sang then Mangue; shops; events): the heal would have counted in full the other way round. One misplay, with the HP lost = min(heal cut, Max HP gained). Not in Erreurs (fight misplays); in the trophy Distrait (every miss). |
| Cards that cost other cards (v0.17.44, Zaille75) | A card that exhausts or transforms other cards of the hand (HORS DE MA VUE !, Marqué au fer rouge, Pacte brûlant, GARDES !!!, Épuration, Récupération, Virilité, Scorie, Feu démoniaque, Second souffle, Attiser les flammes, Furie, Eidolon, Force primitive) counts as **kept on purpose**: not playing it never makes energy left, a free card or a block alternative a misplay (missed lethal still uses the attacks among them). |
| Status damage dealer (v0.17.44) | The game gives the holder as the dealer of a status or curse card's damage (Brûlure, Déclin, Érosion, Toxicité, Infection, Appel, Malchance, Regrets): the creator rule of v0.17.38 now applies to it (enemy → Ennemis, ally → allies, self or deck → Auto-infligés); before, it was always Auto-infligés. |
| Relic shares (v0.17.45, Zaille75) | Every buff or debuff stack remembers the relic (or potion) that applied it. On each hit, the part due to a relic moves from the card's (or pet's) damage to that relic, in the **Reliques** column; the total never changes. Parts: the Strength a relic gave (Vajra, Soufre, Girya, Épée de jade, Fronde du courage, Thé de braises, Mini Régent, Anneau arc-en-ciel, Fard pétillant, Shuriken, Mitaines réchauffantes, Crâne rouge: Strength × the hit's multipliers × the relic's share of the Strength), a relic's own multiplier or bonus (Pointe de stylo…: hit − hit ÷ multiplier), the player's powers a relic gave (Vigueur from Akabeko…), Vulnérable a relic applied (Sac de billes, Perceuse: the extra damage of the turn whose stacks the relic applied), Poison / Épines a relic gave (Entonnoir torsadé, Écailles de bronze), a Lightning / Glass orb's Focus (Disque dur). When several multipliers apply, each takes hit − hit ÷ its multiplier, scaled down so that all the parts together never exceed the hit (approximation, exact total). Block: the Dexterity part of a card's block (Kunai, Pierre étrangement lisse, Boucle de ceinture, Fléchette hélicoïdale), a Frost orb's Focus part and a power's block (Placage from Gorgerette) are the relic's in the block details. Potions work the same way (Potion de force → Potions). Powers from cards (Létalité, Cruauté, Force from Enflammer) stay in the card's damage. |
| Relics that strengthen enemies (v0.17.45) | Soufre and Pierre philosophale give the enemies Strength: on each enemy attack, the extra damage from the Strength the relic gave (× Weak and Vulnérable), for the HP it really took (at most the HP lost), is charged to the relic's owner: on themself **Auto-infligés**, on an ally **Tirs amis** (ally: "par alliés"); listed in the Dégâts details. |
| Caps on enemies (v0.17.45, Zaille75) | Damage a cap stopped — Carapace endurcie at its limit, Dur à cuire (Exosquelette) — counts in **Excédent** (shown in the details). When every enemy is at its Carapace endurcie limit, energy kept with only attacks in hand is **Énergie en trop**, not a misplay; an attack played into a cap already reached (Carapace endurcie, Géant de la Cascade's infinite-HP phase) is **Énergie pour rien**, besides the existing Choix rule when something better was playable. |
| Co-op wrong order, Vulnérable (v0.17.45, Zaille75) | An attack an ally played on an enemy while the player's card that gives that enemy its first Vulnérable was already in the player's hand: the attack should have come after it. The damage it lost is a wrong-order misplay (Choix) for the player who attacked. |
| Pet attacks and pet sacrifices (v0.17.45, Zaille75) | A pet attack (Cliquetis…) left in hand while the pet is dead is no energy misplay, unless the player's own card killed the pet this turn (Détonation osseuse): it could have been played first, so it still counts. A card that kills the player's pet (Détonation osseuse, Sacrifice) is kept on purpose: never energy left, a free card or a kept block card (missed lethal still uses it). |
| Cards that cost HP (v0.18.4, Zaille75) | A card that makes its player lose HP when played (Percée, Offrande, Hémokinésie, Mur de sang, Marqué au fer rouge, Bouclier démoniaque, Saignée) is **kept on purpose**: never the stronger attack of a weak attack, a card the energy left could have paid, a block alternative or a free card (missed lethal still uses it). |
| Wrong order, more boosts (v0.18.5, Zaille75) | Same rules as wrong order, counted in **Erreurs** (no new column). An earlier play is a choice unless it did something besides damage, block **or a debuff on an enemy** (v0.18.5: a debuff no longer excuses the order; draw, energy, own buffs still do). New boosts: **Calcification** (each earlier hit of the player's pet, Titos), **Précision** (each Surin hit), **Lames fantomatiques** (the turn's first Surin), **Un pour tous** (beta: each hit of an X-cost attack), **Pistage** (+50% on enemies with Faiblesse), **Bien attaché** (each block gain of a Défense), **Camouflage** (block doubled), **Conquérant** (Lame souveraine ×2 on its enemy), **Forge** (Lame souveraine played before a Forge card already in hand lost the forged damage), co-op **Encerclement / Renverser** (an ally's attack before the card, like co-op Vulnérable). **Monologue** and **Rupture**: the Strength they give after a non-attack card was lost by the attacks played before it. **Décomposition** (Chasseur-tueur: −1 Strength and −1 Dexterity after each card): a card that is neither an attack nor a block card, played while an attack / a block card was already in hand, cost that attack 1 damage per hit / that block card 1 block per gain ("Éclair+ avant Frappe : 1 dégât perdu"). Damage and block lost follow the usual caps (enemies still alive, block below the incoming attack). |
| Orb-only cards (v0.18.5, Zaille75) | Activation double, Activation multiple and Activation quadruple only evoke orbs: keeping an orb is a choice, so they are **kept on purpose** (never energy left, a block alternative or a free card). |
| Energy gained (v0.18.5, Zaille75) | **Énergie gagnée**: energy a player gains during fights from anything (cards like Sursis, powers like Orbite, orbs like Plasma, relics, potions), each turn's refill left out. The details list it by source. |
| Energy-only cards (v0.17.45) | Cards that only give energy (Alignement, Confiance, Saignée, Sursis, Énergie double, Flux d'énergie, Paré au combat, Rituel oublié, Éclat, Production, Supercritique, Tactique, TURBO, Feu follet) cannot use the energy left: kept on purpose. A card with Retenue (Retain) added by anything is also kept on purpose: the rule reads the card in hand as it is. |
| Orichalque (v0.17.45, Zaille75) | Counted only when the block card gave **less** than Orichalque would have (same block: nothing lost). |
| Card rewards (v0.17.45) | From the game's history, per floor kind (fights, events, others): cards offered, cards taken, offers where none was taken. |
| Event outcomes (v0.18.1, v0.18.4: Ancient choice once without ".title", enchantment once, no shared-event line in solo, Zaille75: "track but don't introduce new stats") | **Log only, no stat or column.** For each event / Ancient floor, once complete, one `event outcome` line per player from the game's map history: option chosen, cards gained / removed / upgraded / downgraded / transformed / enchanted, card offer (taken marked *), relics gained / lost, potions gained / thrown away, gold, HP, Max HP. Shared co-op events: a `shared event` line with every player's vote and the option the game kept (it picks one vote at random), so whoever decided is known. |
| Other players' gold (v0.17.45) | Each game's history records the other players' shop purchases as gold lost: on a shop floor, another player's gold lost counts as spent (Panier percé now agrees on both PCs). |
| Outside combat (v0.17.14) | Stats from outside a fight (campfire, events, map) count only in the Run scope, no longer in the last fight's. |
| Weak attack | An attack played on an enemy while a stronger attack (game's damage on the same enemy, same cost or cheaper) stayed in hand until the end of the turn. **v0.18.4:** the stronger card's cost is the one it had without the played attack: Piétinement costs 1 less for each attack played this turn (the game lowers it as the attack starts), so it is not "same cost" thanks to the very attack it is compared with. Not counted when the played attack could kill its target or did something besides damage (debuff, draw…, **v0.17.1:** or moved another card: Déflagration tombale bringing a card back from the discard pile). |

## E. Later extensions (not in v1)
Records (best combat, fastest kill…), combat duration (turns and real time), potion use,
cards played, energy spent, gold, etc.

### E1. "Lost opportunity" (low priority, v2)
Idea: show what a player could have done instead of something useless.

| Stat | Definition | Reliability |
|---|---|---|
| Unused energy | Energy a player still had when their turn ended **and that was lost** (energy kept for the next turn by a relic or power is not counted). | Exact |
| Energy spent on wasted block | For each block card: its energy cost × the share of its block that ended up wasted or useless. Example: a 1-energy Defend whose 5 block was fully wasted → 1 energy lost. | Exact |
| Missed damage (estimate) | Damage a real attack from the hand could have dealt instead of the wasted block (rule below). Shown with "~". | Estimate |

**Missed damage rule** (no averages: only real cards that were really in hand):
1. When a block card is played, the mod records the attack cards in hand that were **playable** at that
   moment, with the game's own damage preview against each living enemy.
2. At the end of the turn, if some of that card's block ended up wasted, a candidate attack counts only if:
   - it was **still not played** at the end of the turn (otherwise it was not an alternative);
   - its cost ≤ the block card's cost + energy left unused at the end of the turn.
3. The best candidate is kept (highest previewed damage, capped at the enemy's HP + block). Missed damage =
   that damage × the wasted share of the block card. Each attack card can be used for only one block card.
4. No candidate → no missed damage (the player had no alternative).

Known limits (why it stays an estimate): cards that draw cards (what would have been drawn is unknown),
attacks with random or conditional effects, and the enemy's later actions.
0-cost block cards and block from relics/powers cost no energy, so they never create a lost opportunity.

## F. Block attribution: "prevented", "wasted" and "useless help"

### Principle
Help counts only if it **actually saved HP**: the damage would have reached HP without it.

### Rule: block layers, consumed first-in, first-out (FIFO)
1. Each time a creature gains block, the mod records a **layer**: (giver, amount, time).
   Block a player gives themselves is their own layer; block from an ally is that ally's layer.
2. When a hit is absorbed by block, layers are consumed **from the oldest to the newest**.
   The absorbed amount is credited to each layer's giver as "damage prevented".
3. When block disappears without absorbing damage (block reset at turn start, block removed by
   an enemy effect, end of combat…), the remaining layers are counted for their giver as
   **wasted** or **useless** (decision F3 below).

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
| 8 | A has 5 own block. B gives A 5. **Then** A gives himself 5 more. Enemy hits A for 10. | A: 5 prevented (oldest layer). B: 5 prevented. A: **5 wasted** (newest layer, never used). |

### Edge cases
| Case | Rule |
|---|---|
| Block kept across turns (Barricade-like effects) | Layers keep their giver and order; they become wasted/useless only when they actually disappear. |
| Block removed by an enemy effect (not by damage) | Counts as wasted / useless help (it saved nothing). |
| Damage that ignores block (HP loss) | Consumes no layer. |
| Damage from any source absorbed by block (enemy, self-damage, friendly fire) | Counts as prevented: the block did save HP. Shown with its source in the detail. |
| Block **left when the combat ends** | **Counted** as wasted / useless help, like any other block that saved nothing. |
| Block consumed or converted by a card instead of damage (e.g. a card that spends block) | Consumed FIFO, counted neither as prevented nor as wasted, but tracked as "block spent". |
| Block gain modified (Frail, Dexterity…) | The layer records the amount **actually gained**. |
| Block created by a relic, potion or power | The giver is the player who owns that relic, potion or power. |
| Block gained when the player dies/the combat ends in the same instant | Ignored. |

### Decisions
- **F1.** FIFO validated (oldest layer absorbs first, whoever gave it).
- **F2.** Block left at the end of combat is wasted / useless too.
- **F3 (v0.4.0, replaces "useless help").** Unused block is split by whether something better could have been done:
  - At the end of the player's turn, each block **card** played that turn is checked. It "had an alternative" if an
    attack card that was in hand when it was played is still in hand, is playable (only energy aside), and costs no more
    than the block card's energy + the energy left at the end of the turn. One attack can serve as the alternative of
    only one block card. **v0.17.25:** the alternatives go to the newest block plays first (block is used oldest first, so the newest
    is the one left unused).
  - Unused block from a card with an alternative → **wasted** ("gâché"), **only if that card's block absorbed nothing**
    (v0.16.8): block that stopped part of the damage was needed (without it, HP or the pet's HP would have been lost),
    so its leftover is overshoot → useless. Everything else → **useless** ("inutile"):
    no alternative, block from relics/powers/potions/auto-play, or block played on the turn the combat ended.
  - Same rule for block given to allies; the part given to allies is also shown as "…of which given to allies".
  - In the examples above, "useless help" now reads "wasted or useless (given to an ally)".
- **F4 (v0.4.5).** A block card that **also did something else** during its play (applied a power or debuff, dealt
  damage, healed, drew cards, discarded or exhausted another card, channeled an orb, created a card, summoned, upgraded a card or offered to (Armaments),
  gained stars (Gather Light)) is never wasted: its unused block is useless.
  Known limit: an effect triggered by something else during that play (a relic reacting to it) also counts.
- **F6 (v0.4.7).** The alternative is not only an attack: any **useful card** counts, meaning an attack, or a skill or power
  that gives no block (debuff, draw, buff…). Status cards, curses and other block cards are not alternatives.
- **F4 audit (v0.9.1): every card of the game that gives block (80 cards, beta branch) was checked.**
  - Side effect detected by a generic hook (so new cards are covered too): power/debuff applied (Blur, Flame Barrier,
    Leg Sweep, Taunt…), damage (Iron Wave, Dash, Blood Wall…), draw (Backflip, Shrug It Off…), another card discarded,
    exhausted or **moved between piles** (Survivor, True Grit, Second Wind, Hologram, Cosmic Indifference, Compact),
    orb channeled (Glacier…), card created (Cloak and Dagger, Fight Through…), summon (Pull Aggro), upgrade (Armaments,
    Compact, Manifest Authority), stars (Gather Light), **energy** (Constellation), **Forge** (Bulwark), **Sly given**
    (Hand Trick).
  - Special cases, never wasted: **Genetic Algorithm** (its block grows each time it is played) and **Sacrifice**
    (kills your own pet; validated by Zaille75, v0.10.3).
  - Pure block (can be wasted): the Defends, Deflect, Leap, Cloak of Stars, Particle Wall, Ultimate Defend, Untouchable,
    Melancholy, Entrench, Stack, Rally, Lift, Boot Sequence, Impervious, Minion Sacrifice, Mimic, Mirage, Expect a Fight,
    Death's Door, Evil Eye, I Am Invincible, Mad Science (block version, unless its rider does something). An attack is an alternative only if the player could afford the Thorns damage it causes: the
  block left at the end of the turn minus the incoming attack must cover it (Thorns × hits; on all enemies for an attack
  on all enemies, else the enemy with the least Thorns). Otherwise not playing it was right, and the block is useless.
- **F7b (v0.17.20, Zaille75) — punishing enemies, like Thorns.** An attack on an enemy with Personal Hive (Apicultueur:
  each hit adds a Dazed to the deck) is never counted as a card that should have been played (alternative of a wasted
  block, unused energy, relic trigger): a single-target attack only when every enemy it could hit has it, an attack on
  all enemies when any of them has it — unless the attack would kill those enemies.
- **F5 (v0.4.6).** An **Ethereal** block card is never wasted either (not playing it would have lost it anyway).
- **F9 (v0.17.0) — Plating (Blindage).** Plating gives its block at the end of the turn, and the amount is known in
  advance. Its layer counts as the **oldest** one: it absorbs first. So a block card the Plating made unnecessary (the
  Plating alone covered the attack) absorbs nothing and can be wasted (if a useful card stayed in hand, rule F3).
  Before v0.17.0 the card's block absorbed first and the Plating's block was counted as useless instead.
- **F10 (v0.17.32, Zaille75) — replaceable block absorbs last.** Damage takes the block of the plays that had an
  alternative (rule F3) **last**: orbs (Givre), relics, powers and the block cards that had no alternative are used
  first, oldest first. So when the block of the turn was more than needed, the leftover is the replaceable card's, and it
  is wasted: Défense + Défense + Givre orbs against 14 damage with Frappe in hand, 19 block, the second Défense is gâché
  (before: the orbs' block was left over and counted as useless).
