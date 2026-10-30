# Handoff — state of the project (2026-10-30, v0.18.0)

> **Next session starts with Zaille75's next co-op test (beta branch, v0.17.38 or later).** He will send it with
> `Envoyer-a-Claude.bat` → `logs/inbox/<date_time>/` (both logs + F9 `feedback/` folders). **Review and summarize every
> report first, wait for his go, then implement the whole batch** (memory rules). See "Waiting for the next co-op test"
> below for what to check.

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
- **In-game reports (F9, v0.17.31, development builds):** Zaille75 sends everything with a double-click on
  `Envoyer-a-Claude.bat` (from `dist/tools/`, git-ignored: it holds the server address): each send is a folder
  `logs/inbox/<date_time>/` with his `feedback/` folder of reports + both logs. **Always review and summarize first, and
  wait for his go before changing code.** Each report folder has `message.txt`, `screenshot.png` (read it with the Read tool),
  `state.txt`, `run.txt`, `log-tail.txt`, `errors.txt` (v0.18.0), plus a zip of the folder. After reading, move them
  to `logs/feedback-<date>/`. Since v0.18.0 reports are anonymised (players are "Joueur N", ids "id-joueur-N").
- **Public reports (v0.18.0, 1.0 players):** the public build (`-p:PublicRelease=true`) keeps F9. "Créer le rapport"
  makes the zip, opens its folder and the GitHub issue form (`.github/ISSUE_TEMPLATE/report.yml`); the player drops the
  zip on the issue. Read them with `gh issue list --label report` once `gh` is installed. Same rule: summarize, wait for
  Zaille75's go.
- **Privacy before anything public on GitHub (Zaille75, 2026-10-30):** check that no personal info is in the files or
  history: friends' nicknames, Steam ids, Windows user names, server address, local paths. Real values live only in
  `docs/HANDOFF.local.md` (git-ignored). Tests use neutral names ("Kiki", "Max").
  - Read it by grepping: `EVENT <Type>`, `===== COMBAT SUMMARY`, `block play N:` lines, and errors.
  - Then archive it as `logs/<date>_v<version>_<short-topic>.log`. `logs/` is git-ignored because it contains friends' Steam IDs.
- **Every change:**
  1. Update the spec first (`01-metrics.md` for rules; `02-ui.md` for the UI, with a "vX.Y.Z:" change note).
  2. Bump the version in **both** `src/KikiMAIster/KikiMAIster.json` (`"version"`) and `src/KikiMAIster/KikiMAIster.csproj` (`<Version>`).
  3. Run `./build.sh beta`. It runs the build, PatchCheck (each patch must match exactly one game method), and the unit tests. Then commit.
  4. Give Zaille75 the install command (PowerShell, game closed). The real login and server address are in
     `docs/HANDOFF.local.md` (git-ignored, this machine only):
     ```powershell
     $m = "<game folder>\mods\KikiMAIster"
     scp "<user>@<server>:<repo>/dist/beta/KikiMAIster/*" "$m"
     ```
- The mod writes `KikiMAIster.log` next to its DLL, in the game's `mods\KikiMAIster` folder on Windows. Zaille75 copies it from there.
- **Git:** private GitHub repo `git@github.com:Zaille75/KikiMAIster.git` (`origin`, SSH key `~/.ssh/id_ed25519`).
  Commits are authored `Zaille75 <52042243+Zaille75@users.noreply.github.com>` (repo-level git config): never put
  Zaille75's real name, login or server address in tracked files.

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
- A wrapping `Label` (AutowrapMode) needs a `CustomMinimumSize` width, else it measures one word per line and its container grows to the screen height.
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

## Path to v1.0 (agreed 2026-10-28, besides refining the mod)
Order: test → default branch → release prep → publish → maintenance. The GitHub backup is done (private `origin`).

**What Claude needs from Zaille75, step by step:**
1. **In-game test (blocks everything):** one full **co-op** run with v0.16.8 (nothing co-op since v0.8.0 is checked in
   game: Permis, Encaissés, Ange gardien, per-ally details, co-op trophies/records, compositions), ideally plus one solo
   run. Then "log transferred" + screenshots of anything odd. Run-end = "full run without data loss" (roadmap phase 4).
2. **Default (main) branch — done (v0.17.22):** `lib/default/` holds the main branch v0.107.1 (decompiled in
   `lib/default/decompiled/`, namespaces as folder names). `./build.sh default` → `dist/default/KikiMAIster/`. Branch
   differences are `#if GAME_DEFAULT` blocks (spec 03-technical). The old beta files are kept in `lib/beta-v0.111.0/`.
   Zaille75 only switched to the main branch to get its files; he plays the **beta**: give him the **beta** install
   command (unless he says otherwise).
3. **Release prep (decided 2026-10-28, done):** icon `docs/images/icon.png` (drawn by `tools/icon/make_icon.py`),
   MIT license, README (English + French in one file), CHANGELOG, manifest description updated. Name stays
   KikiMAIster. The GitHub repo becomes public at 1.0 (checked: no game files, Steam IDs, login or server address in
   the files or the history). Version 1.0.0 is set when publishing.
4. **Publishing (with 1.0):** **Steam Workshop first** (Zaille75: it is how the game wants public mods; the game
   loads subscribed Workshop items itself, app 2868840), then a GitHub release (and Nexus Mods if wanted). No
   official StS2 uploader found yet (StS1 shipped a `mod-uploader.jar` in the game folder): ask Zaille75 to look for
   an uploader in the game folder, else use SteamCMD `workshop_build_item`. Former note: which sites (GitHub release, Nexus Mods, Steam Workshop if StS2 accepts mods there). Claude checks
   each site's requirements first; Zaille75 creates the accounts / uploads where a login is needed.
5. **Maintenance after each game patch:** Zaille75 copies the new DLLs into `lib/beta/` (and `lib/default/`), Claude
   rebuilds (PatchCheck flags broken hooks and, since v0.17.40, renamed private members listed in `ReflectionTargets.cs`),
   fixes them, re-syncs `BlockAlternatives.HitCount`, bumps the version.

Optional roadmap items (only if asked): JSON export, end-of-run Totals / Per act tabs, MVP line, opacity / scale /
hotkey settings, chart history across Save & Quit.

## v0.18.0 — reports for every player (2026-10-30, not tested in game)
F9 in every build, a "Signaler un problème (F9)" button in the settings, FR/EN window, anonymised files
(`Core/Anonymizer`, tests), zip, `errors.txt`, error hint at the bottom of the panel, GitHub issue form; the DLL keeps no
local path (`PathMap`, no SourceLink). To check in game: F9 then "Enregistrer" (dev build) writes the folder + zip, and
the files show "Joueur 1/2" instead of names in co-op; the ⚠ line shows after an error and goes away after a report.
**Open before going public:** the git history still holds friends' nicknames in older commits (see the end of this
conversation's report); decide whether to rewrite it or publish a fresh history.

## v0.17.45 — co-op runs of 2026-10-29 (not tested in game)
Inbox `logs/inbox/2026-10-29_22-44-39/` (player 1, main, v0.17.44: two co-op runs with Max, 31 fights) + Max's reports in
`logs/inbox/feedback-2/`. Full card-by-card check of the log: every stat recomputed from the events matched the mod (run 1 at
21:29, run 2 at the end), block values matched the cards, all damage differences came from game effects. Fixed: player names
failing at run start (Player#1 / Steam ids), Cliquetis counted with Osty dead, Sursis named as a card for the energy left,
Orichalque with the same block, other player's shop gold read as lost. New: relic shares (Reliques), Soufre's harm, caps in
Excédent, co-op Vulnérable order, card rewards, chifumi moves / longest / trophies, fun trophies, Godot (map votes), Échap
hides the windows. Check in the next logs: `PowerChanged … Origin = Relic:…` on relic buffs (the origin comes from a stack walk
in `CombatHistory.PowerReceived`: if it stays empty, the relic shares stay 0), `map vote: … chose last`, `DamageCapped`,
`EnemyStrengthHit` with Soufre, the Reliques column moving and the totals unchanged; both PCs' Panier percé.

## v0.17.44 — partial check of the Régent run (2026-10-29)
Partial card-by-card check of `logs/inbox/2026-10-29_20-06-37/` (no hands in that log): one bug, status / curse damage always
Auto-infligés (the game passes the holder as dealer) → fixed; Zaille75: HORS DE MA VUE ! kept is not a misplay → cards
that exhaust / transform hand cards are kept on purpose. Everything else matched the cards' code. The full check needs
a v0.17.43+ log.

## v0.17.43 — play-by-play log (2026-10-29, not tested in game)
`PlayLog.cs` (see 03-technical). Next step agreed with Zaille75: when he says so, a **partial card-by-card check of his
last run** (`logs/inbox/2026-10-29_20-06-37/`, v0.17.40: no play lines, card names only from damage / block sources);
then the full check on the first log written by v0.17.43. Report differences first, change nothing before his go.

## v0.17.42 — Régent solo run feedback (2026-10-29, not tested in game)
Log `logs/inbox/2026-10-29_20-06-37/` (main branch, v0.17.40, A9, 15 fights). Fixed: the map history read crashed on every
fight room since v0.17.38 (`GetByIdOrNull<EventModel>` on an encounter id) → no HP lost / healed outside fights, 17,000
log errors; a failed read is no longer retried until a floor is added. New (Zaille75's 6 reports): Excédent column +
Bulldozer / Chirurgien; Gâché "overkill" / "overkillFollowUp" (Trou noir); Slow order between attacks (co-op too, ≥ 1
damage); "Hors combat" misplay (Max HP after a capped heal, `maxHpAfterHeal`); fight time stops at the last enemy's death;
`UI/HoverTip.cs` (trophies window hover texts to the right). Check in the next logs: `max HP after a capped heal`,
`MissedChance … overkill`, no `RunHistory.Read` error, and `run history …: HP lost outside [...]` filled.
Zaille75 switches between beta and main: give the install path he asks for.

## v0.17.41 — review section 4 (2026-10-29, not tested in game)
The end-of-turn misplay rules moved from `Patches/BlockAlternatives.cs` to `Core/TurnReview.cs` (pure, 10 tests in
`TurnReviewTests`); the patch fills a `TurnState` snapshot. Same rules, so the figures must not change: in the next logs
compare `block play …`, `wrong order …`, `stars …` lines and the Choix / Gâché counts with what Zaille75 saw. New log
line at each fight's end when some damage / block had no known source: `sources unknown this fight: …`. Small known
change: Mimic no longer counts as block for one ally in the Stars check.

## v0.17.39 — review section 3, items 4–7 (2026-10-29, not tested in game)
Enchantment damage, HP lost by kind, heals by source (details only), fight length and fight records
(`KikiMAIster.fights.cfg`, `UI/FightRecordStore.cs`, `Core/FightRecords.cs`). Check in the log: `fight record …` lines after
each win; the "★ Nouveau record" line in the combat summary; enchantment damage uses the game's private
`Hook.ModifyDamageInternal` by reflection (if it is renamed, the detail is silently empty). Items 1–3 of section 3 (event
outcomes, stolen cards, afflictions) not requested.

## v0.17.38 — code review fixes (2026-10-29, not tested in game)
Full review of the mod against the decompiled game: `docs/REVIEW-2026-10-29.md` (findings, game code, fixes). Zaille75's
decisions: status / curse damage goes to whoever created the card (enemy / self / ally); one kill per enemy and enemies
whose death does not end the fight left out of missed lethal; minions dying with their leader credit nobody; Ancients'
heals shown apart ("Anciens"); Oubli's Dexterity is "Armure permise". In the next logs, check:
- `MissedChance … growth` lines now appear (Algorithme génétique, La faux… never played).
- Sacrifice: no jump of "Familier" / PV sauvés; Burn / curse damage in Ennemis or Auto-infligés, no longer Autres.
- Co-op: gâché / unused energy for the **other** players look right (incoming attack per player); both PCs agree.
- Évités: Intangible (Fantôme dans un bocal) appears; Strength down on cultists (Rituel) counted.
- `run history <id>: HP lost outside […], healed outside […]` lines: "afterFight" (Sang ardent) and "ancient" present, no
  ~Max HP heal on Neow's floor.
- Extra turn (Ambre gris, Œil de Paël): block played before it is never "needed".
- Not done (ideas from the review, section 3): event outcomes, stolen cards, afflictions, enchantment damage, damage
  taken by kind, fight length, heals by source. Section 4: moving `BlockAlternatives` decisions to `Core/` for tests.

## Waiting for the next co-op test (written 2026-10-28, end of session)
Versions v0.17.32 → v0.17.37 were built from two solo runs on the **main branch** (Défectueux, then Soldat de fer and
Régent; archived in `logs/2026-10-28_v0.17.31_main_solo_defect_inbox/` and
`logs/2026-10-28_v0.17.33_main_solo_ironclad_regent_inbox/`). **None of it is tested in game yet.** In the co-op log,
check especially:
- **F10 (v0.17.32):** block from a play with an alternative absorbs last — gâché / inutile figures and `block play N`
  lines look right, nothing shifted between players (co-op block given to allies goes through the same ledger).
- **New Choix / Gâché kinds:** `restBoss` (+ Pantographe), `order`/`orderBlock`/`orderDark` from Focus, `stars`,
  `starsWasted`, `shell` (Carapace endurcie: hits with 0 damage are now reported before the early return in
  `DamagePatches.Report`, and InfiniteWithNumbers enemies are no longer skipped — check the damage totals did not jump).
- **Unused energy:** no `energy` miss while an enemy is asleep or when only Thorns-punished attacks are left; Exhaust
  cards are never a miss (KeptOnPurpose) — check weak attacks / relic triggers did not disappear wrongly.
- **Run history:** `RunHistoryRead` now carries HP lost/healed outside fights by floor kind and `RunEconomy` (gold,
  curses, purchases); `event before the fight` log lines for event fights (PreFightEvent, once per floor).
- **UI:** Erreurs column in every tab; Inutile "x/y"; Dégâts "Hors combat" / "Soins hors combat"; section
  "Or et malédictions"; Cartes et potions "Énergie pour rien"; trophies Chercheur d'or, Panier percé, Radin, Chat noir,
  Batterie pleine (spare + for-nothing energy).
- Zaille75 plays the **beta** normally: install from `dist/beta/`. `dist/default/` is only for main-branch tests.
- Send-to-Claude script lives in `dist/tools/` (git-ignored, has the server address); rebuild with the same content
  if `dist/` is ever wiped.

## Open topics for the next conversation
- **"Ce groupe" differs between players (2026-10-29, kept open by Zaille75):** each PC reads its own game run history (one player
  524 runs, the other 149), so the group stats differ. Fixing it would need sending the history between players.
- **Event outcomes not tracked yet (audit v0.17.35, decision pending with Zaille75):** cards gained / removed, upgrades,
  transforms, enchantments, relics gained / lost / traded, potions gained / thrown away, which event option was chosen,
  who decided in the 8 shared co-op events (JungleMazeAdventure, DenseVegetation, MorphicGrove, PunchOff,
  TheLanternKey, BattlewornDummy, FakeMerchant, WarHistorianRepy). All in the game's map history. Gold and curses done
  in v0.17.36; HP lost / healed in v0.17.34–36.
- **v0.17.1:** the log (`logs/2026-10-27_v0.16.8_coop_regent_necrobinder_after_crash.log`) only covers the session
  after the crash (fights 9–15: it was overwritten at the restart). Fixed from it: 10, 15, 22, 23, 27. Answered 13:
  run totals were restored from the file saved after fight 8, so the crashed attempt never reached the Partie stats.
  14 and 24 fixed in v0.17.3 from the game's own logs (`logs/game/`, copied from
  `C:\Users\<user>\AppData\Roaming\SlayTheSpire2\logs`; they contain the mod's lines too and keep older sessions).
  The "crash" was no crash: the game waited for the other player to end turn 6 (Mante Chapardeuse), then was quit normally.
  Since v0.17.1 the mod also keeps `KikiMAIster.previous.log`.
- **First co-op run with v0.16.8 (2026-10-28): 27 feedback points.** v0.17.0 did the ones that did not need the log
  (1–9, 11, 12, 16–21, 25, 26: see 02-ui v0.17.0). **Waiting for the log** ("log transferred") for: 10 (Âme sœur cards
  created in the other player's deck not in Coop), 13 (crash mid-fight: did the Run stats keep the first attempt?),
  14 (fight 1 T2: a player got 2 PV sauvés, expected 0–1), 15 (Déflagration tombale counted as a misplay), 22 (unused
  energy counted when the block was not needed), 23 (Esquisse not in "Cartes données"), 24 (missed lethal fight 4 T4),
  27 (Max's many "À l'attaque !" in Affaiblissements; then clear that record: `RecordBook.Clear("trophy.debuffs")`).
- New trophy names (Culturiste, Peau sur les os) are proposals.
- Main branch solo Regent run (v0.17.22, `logs/2026-10-28_v0.17.22_main_solo_regent_15fights.log`): no errors, all
  damage sources known, history reads fine. Records per ascension level since v0.17.24.
- Solo Necrobinder run (v0.17.8, `logs/2026-10-28_v0.17.8_solo_necrobinder_15fights.log`, death fight 15 Ovicoptère):
  no errors. Fixed: extra damage (Dégâts + / Permis) counted block and overkill (v0.17.19). Personal Hive
  (Apicultueur) now treated like Thorns (v0.17.20, validated).
- v0.17.4–0.17.6 (pet rules, Zaille75): debuff-saved pet HP count once the pet died and the owner lost HP; Déchaînement /
  Protecteur damage from Osty HP saved/given by an ally is Permis; Sacrifice block from ally-given Osty HP is "Armure
  permise" and what it stops is PV sauvés. Answered (v0.17.7): Ralenti stays in Choix; trophy names OK; self-debuffs
  go to Dégâts "Auto-infligés"; growing cards to Choix; "Évités" chart removal confirmed; v1.0 prep later. The Mante
  freeze: the game just stopped (frequent beta crashes). **Still open:** block on the pet waiting for its death (question
  re-explained → done in v0.17.8, judged in game; back-up tag `backup/v0.17.7-pet-protection-immediate`);
  Agression dropped (Zaille75 will check later); 3–4 player per-player table done (v0.17.8).
- Zaille75 tests v0.16.8 in co-op and sends the log ("log transferred"), see "Path to v1.0" step 1.
- **Trophy list:** v0.15.0 co-op additions validated by Zaille75 (Bouclier humain, Dynamo, Croupier; Forteresse = own only;
  Traître includes ally HP that a kept ally block card could have saved; Tank and pets given to allies credited). Not tested in game.
- Previous fights are kept across Save & Quit since v0.14.0 (run file `combat.N.*` lines); not the explanations of misses.
- Intouchable: victories only, hidden after a defeat (validated, v0.14.1).
- Roadmap leftovers: MVP line in the summary; end-of-run Totals / Per act tabs; JSON export; settings for opacity,
  scale, hotkey; support for the default (non-beta) branch (`lib/default`); publishing (GitHub, Steam Workshop, Nexus).
- Offered but not requested: keeping the chart history (per-combat timeline) across Save & Quit.
