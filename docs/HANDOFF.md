# Handoff — state of the project (2026-11-14, v1.2.1)

> **Start here in a new conversation**, then `CLAUDE.md`, `docs/specs/01-metrics.md` (rules) and `docs/specs/02-ui.md`
> (interface). The history of every version is in `CHANGELOG.md` and the specs' "vX.Y.Z" notes; the git history has the
> older handoff sections (v0.7 → v0.17).
>
> **State (2026-11-14):** **1.2.1 released** on GitHub (tag `v1.2.1`, by WTT27) and the Steam Workshop (uploaded by
> Zaille75, item 3832591950 "KikiMAIster - Solo and Co-op stats"). `main` = released code, no open branch
> (`feature/fool-arrows` merged). 1.2.1 is **not tested in game yet**: Zaille75's two games of 2026-11-13 were played with
> 1.2.0 (their 12 reports are all handled in 1.2.1). One DLL for beta v0.113.0 and main v0.111.0.
> **1.2.2 (2026-11-14, merged in `main`, not tested in game):** old main v0.107.1 support dropped
> (`GameBranch.IsBeta`, the main-only patches, the `GameApi` damage delegates, beta-only reflection targets and the lobby
> reflection removed). The Fool's melodies and more arrow mistakes (spec 01-metrics rows "The Fool's melodies" → "Tir répété at
> 0 energy", 02-ui "1.2.2"): Core `FoolRules.cs` (partial `TurnReview`), capture `BlockAlternatives.Melodies` (`[BranchOnly]`) and
> new `Arrows.Fill` fields (`ArrowHitsLeft`, `CardBonus`, `QuietusAtStart`, `HitDamage`), `TurnPlay.Created`
> (`OnCardGenerated`). New kinds: Choix "melodyFirst", "melodyLate", "retrievalTarget", "retrievalOrder", "payoffTarget",
> "repeatShotEmpty", "repeatShotShort"; Gâché "verseUnused"; misses ending with a loss (`Overlay.LossKinds`, `why.lost*`); Vers
> Ki on a weaker card (`VerseAtStart`), Chorale Cho's auto-plays (`AutoMelody`). PatchCheck beta 78, main 117; tests 233.
> Zaille75 tests 1.2.1 + 1.2.2 together in game.
> When Zaille75 sends a new inbox (`logs/inbox/<date_time>/`): **review and summarize every report first, wait for his go,
> then implement the whole batch** (memory rules). Each send now also brings `KikiMAIster.names.txt` (game names in French):
> copy it to `lib/beta/names.txt` when the game version changed.

## Next session — what to do
1. **Wait for Zaille75's next inbox** (games with 1.2.1). Check in the logs: no internal error; `empty shot: …` lines;
   the new misses `notImpaled`, `impaleFirst` (also with an ally's Shot, card shown "(Serlfa)"), `shootFirst`, `buffUnused`
   (Estocade), `makerFirst` (Abondance & co.), `coveringFireLate`; wrong-order losses for Fermeté Cho, Flèches sifflantes,
   Visée, Flèches barbelées; **no** "energy left / free card" for Rappel / Empoignade / Déraciner without the player's own
   arrow, nor for a red Shot; no wasted-block alternative that is a red Shot; Galvanisé cards not suggested; panel no longer
   closes hover texts (rebuilds when the mouse leaves); l'Insensé yellow; run title "… · A<n>".
2. **Fool ideas:** done in 1.2.2 (melodies, Récupération héroïque, payoff target, Estocade, Tir répété, Abondance's Feu de
   couverture, Vers Ki on a weaker card, Chorale Cho's auto-plays). **Tarot cards** (Forme mythique): Zaille75 waits for
   in-game play before deciding (all are 0-cost Exhaust tokens, so never a "free card" miss today). In the next inbox also
   check the 1.2.2 miss kinds (above) and `melody first` / `melody late` / `retrieval` / `repeat shot` log lines.
3. **Known limits of the arrow rules** (estimates stay minimums): a Shot that kills gives its arrow back (ignored in the
   lethal checks); Visée's bonus is not scaled by Vulnérable / Tête chercheuse; an ally's arrows ready are not known
   (an ally's later successful Shot is taken as "it had one").
- **Talking to Zaille75:** French in-game names (table `lib/beta/names.txt`, e.g. Clouer = Nail, Estocade = Quietus,
  Fermeté Cho = Cho Firm, Flèches sifflantes = Whistling Arrows, Le Pape = The Hierophant, Tête chercheuse = Honing,
  Flèches barbelées = Serrated Arrows, Visée = Aim). He plays co-op with "Serlfa" (both l'Insensé in the last games).
- **Release steps** (Zaille75 runs the publishing himself): bump `KikiMAIster.csproj` + `KikiMAIster.json`, date the
  CHANGELOG, `./build.sh release`, `python3 tools/workshop/make.py "<change note>"` (update its `ITEMS` text), notes in
  `dist/release/notes-<v>.md`; give him the `git tag … && gh release create …` command (run with `!` here) and the
  PowerShell Workshop command (copy `dist/workshop/*` to `C:\KikiMAIster-workshop`, run `Publier.bat`).

## 1.2.0 — game update beta v0.113.0 / main v0.111.0 (2026-11-13, released on GitHub, merged in main, not tested in game)
Beta v0.113.0 (12 Nov, "biggest beta patch": the Fool, Blitz, Dread, ~140 cards changed); the main branch moved to v0.111.0
the same day (= our former `lib/beta`). Patch notes: Steam news API (`ISteamNews/GetNewsForApp`, appid 2868840): the
Steam page / untapped / SteamDB block automated fetches.
- **lib:** `lib/beta/` = v0.113.0 (from Zaille75, `lib/beta-v0.113.0/` raw copy), `lib/default/` = v0.111.0,
  `lib/default-v0.107.1/` = old main (no longer supported since 1.2.2). Both decompiled with `ilspycmd -p -o <dir>/decompiled <dir>/sts2.dll -r <dir>` (same layout:
  `diff -rq lib/default/decompiled lib/beta/decompiled`). `lib/beta/names.txt` is the v0.113.0 table (from Zaille75's game, 2026-11-14,
  the Fool's French names included; `Envoyer-a-Claude` now sends `KikiMAIster.names.txt` each time).
- **Fixed:** `GameBranch.IsV113`; `GameApi.ShouldTakeExtraTurn`; damage funnel `DamageCommandV113` and kill funnel
  `KillCommandV113` (without them no damage / kill credit on v0.113.0); `HitCount` reads "HitCount" (v0.113.0 renamed
  "Repeat" for Coup double, Ricochet, Sept étoiles…: they counted 1 hit), `REPEAT_SHOT` (X cost) unknown; Paré au combat
  out of EnergyOnly. PatchCheck: beta 80, main 84, old main 88; tests 189.
- **Checked, no change:** patched methods whose body changed (`PowerCmd.Apply`: player debuffs applied on the player's
  own turn now tick normally, the mod reads amounts from the game; `RunManager.OnEnded` now guards a second end itself;
  `GenerateRelicFight` got a relic index; `StampedePower` uses another RNG); Prisme infesté still makes cards Souillé
  (Étincelle vitale); Un pour tous now also boosts autoplayed 0-cost cards (rule unchanged); Hook.AfterDeath gained a
  card parameter (patch binds by name); new campfire options (EVOLVE for Byrdpip, RESTORE for the Wuppy relic) go
  through `IsEnabled`; `GameMode` enum: Custom gone, Dread / Blitz added (the mod only names Standard).
- **Zaille75's answers (2026-11-13), done:** (1) Blitz / Dread: their own stats (`GameModes`, per-mode files for
  records, fight records, compositions; F8 "Mode" row); (2) Wuppy like Titos (already: it is a real pet,
  `PlayerCmd.AddPet`; nothing Osty-specific in the damage path); (4) the Fool's most obvious mistake: Shot with no arrow
  ready ("emptyShot", `TurnPlay.EmptyShot` from `Hook.AfterArrowShot` with a null arrow; `TurnCard.NoArrow` /
  `BlockAlternatives.NoArrowReady` keep such Shots out of suggestions and estimates). Tests 192.
- **Second batch (2026-11-13), done:** Or section + gold trophies hidden in Blitz (`Trophies.NoGold`, `Category.Gold`);
  trophy Pie voleuse / Magpie (`trophy.relics`, `RunEconomy.Relics` = `Player.Relics.Count`); two pets per owner
  (StatsEngine pet maps keyed by `Actor.Pet(owner, creature)`, `PetKey` adopts the unidentified `(owner, 0)` entries;
  `PetHpGiven.Pet` / `PetHpBlock.Pet`; Déchaînement order only for Titos); Paré au combat never gâché
  (`IsAttackDiscount` → HadOtherEffect) and "attackDiscount" (Choix, `TurnPlayer.AttackDiscount`, `TurnPlay.CostsX`).
  Tests 195. Workshop description: 1.2.0 is a first quick update that will evolve quickly.
- **1.2.1 (merged in main 2026-11-14, from `feature/fool-arrows`):** the Fool's arrow cards (spec 01-metrics
  "The Fool's arrows"). Arrows never come back at turn start (only 3 powered hits of their owner on the enemy, its death,
  Rappel / Déraciner / Empoignade). Capture: `BlockAlternatives.Arrows` ([BranchOnly], v0.113 types) fills `TurnPlay.ShotArrow /
  ArrowsReady / OwnArrowIn / ArrowIn / SerratedBonus / AimAtStart / AimFactor / NotImpaled / KindCount` and `TurnPlayer.
  ArrowsReady / OwnArrowIn / UnusedBuffs`; `TurnCard.NoArrow` now = "does nothing now" (red Shot, Clouer / Rappel / Déraciner /
  Empoignade without a target), `TurnCard.Shot`; `Lethal.Find(maxShots)`. Core: `TurnReview.ArrowOrder` ("impaleFirst",
  "shootFirst", Serrated / Fermeté Cho / Whistling / Visée order losses), `DidNothing` ("emptyShot" / "notImpaled", relic
  exception), `BuffsUnused` ("buffUnused", Estocade). Inbox 2026-11-13 (12 reports, `logs/inbox/2026-11-13_23-43-05`,
  played with 1.2.0): arrows in co-op (`AlliesHandAtPlay`), Visée on Glissant kept (`Arrows.AimOnSlippery`), "makerFirst"
  (`TurnReview.CardMakers`), "coveringFireLate" (`TurnPlay.AllyBlockPerShot`), Galvanisé kept on purpose, FOOL color, A level
  in the run title, no panel rebuild under the mouse (`Overlay.MouseOverPanels`). Tests 211.
- **Fool ideas not done:** see "Next session" above.
- **Seen in game (2026-11-13, 1.2.0, beta co-op):** no internal error, damage / kills credited, Blitz not played yet;
  still to see: F8 "Mode" row with a Blitz / Dread run, main branch `game branch main`.

## 1.1.1 — GitHub reports of 2026-11-05 (2026-11-06, released on GitHub, not tested in game)
First two public F9 reports (issues #1, #2, by Zaille75 as WTT27; zips added as comments, files in `logs/feedback-2026-11-05/`).
Read them with `gh issue list --label report` (label created 2026-11-06), zips: `gh issue view N --json comments`.
- **Vulnérable gardé** (`TurnReview.VulnerableLeft`, kind "vulnLeft", Choix): Tope là kept while plain attacks were played.
  Damage of this turn only; "et Vulnérable N pour le tour suivant" shown, no estimate of next turn (Zaille75). Orbs never
  count (Vulnérable doesn't boost them: checked, the mod uses the game's own multiplier, and order rules use card hits only).
- **Place bonuses** (Létalité, Géantification, Pointe de stylo, Vigueur, Lames fantomatiques) recorded per hit
  (`EnemyHit.Place*`, from `DamageModifier`s via `BlockAlternatives.SetPlaceBonus`); `TurnReview.SwapGain` for the Slow
  order; same idea for "attack before a Vulnérable attack" (`CheckOrder`).
- **Hibernation:** `FrostOrb.Passive/Evoke` prefixes set an `AsyncLocal` orb owner; Givre block on allies is the owner's.
- **Upgraded cards** in misplays: key suffix `^` / `^N`, shown "+" / "+N" by `GameNames.Of`.
- **F9 save only** when `send-to-claude` exists in the data folder; `dist/tools/Envoyer-a-Claude.ps1` creates it (Zaille75
  must copy the new script to his Desktop). GitHub form: right folder, "wait for the zip's link", reminder in the text.
- **To test in game:** Tope là kept → miss in Choix; Létalité + Ralenti → no miss; Hibernation → Coop "armure donnée" for
  the Défectueux; F9 → no browser/folder after running the new Envoyer-a-Claude once.

## 1.1.0 — one build for both game branches (2026-11-03, released)
Spec 03-technical "Game branches". Compiled against `lib/beta/` only; `GameBranch.IsBeta` (`CardPlay.Player` exists)
picks the path at runtime; the 16 `#if GAME_DEFAULT` blocks are gone. Patch classes per branch use `Prepare()`
(`LoseBlock`/`LoseBlockMain`, `ModifyDamage`/`ModifyDamageMain`, campfire `LocalLeft`/`RemoteLeft`/`RoomLeft`) or
`TargetMethod()` (`TurnEnd`, `DamageCommand`); signature differences go through `GameApi` (`GameBranch.cs`);
beta-only types only inside `[BranchOnly]` methods (`SoulBinder`, `IsOneForAll`, `GameApi.Beta*`); lobby players by
reflection (`CompositionWindow.LobbyPlayers`: the default branch's `LobbyPlayer` was a difference the `#if`s missed).
`ModEntry` applies each patch class on its own and logs `game branch beta|main, N patches`.
- `./build.sh [release]` → `dist/KikiMAIster/`, zip `dist/release/KikiMAIster-<version>.zip`. PatchCheck runs on the
  DLL with both `lib/beta` and `lib/default`: patches (skips `Prepare() == false`), reflection targets, `GameApi`
  delegates, and every game type / member the IL names (allowed only in `[BranchOnly]` methods). It stubs
  `Sentry.Godot` (sts2's module initializer) in memory.
- Workshop (`tools/workshop/make.py`): one item, the former beta item (3832591950), now **"KikiMAIster"** for both
  branches. The "default branch" item (3832592056) had no subscribers: Zaille75 made it invisible (2026-11-03), it is
  no longer updated and will be deleted.
- Workshop upload: SteamCMD printed no "PublishFileID" line this time, but the Steam page was updated (title,
  description). `Envoyer-a-Claude` updated on Zaille75's PC (now on his Desktop), reads `%APPDATA%` (checked 2026-11-03).
- **Tested (2026-11-03):** on the beta branch AND on the default branch (Steam → Properties → Betas):
  the log's first line says the right branch, no "patch … not applied" error, a fight with block lost / campfire /
  F8 in the lobby works.

## v1.0.0 released (2026-11-02)
Tag `v1.0.0` (= `db4f751`, new card icon), repo **public**, release
https://github.com/Zaille75/KikiMAIster/releases/tag/v1.0.0 with the beta and default zips, published by **WTT27**.
- **Accounts:** releases are made as **WTT27** (`gh` on this machine is logged in as WTT27). WTT27 is a collaborator
  with write access, not admin: repo settings (visibility, etc.) can only be changed by Zaille75 on github.com.
  `git push` uses this machine's SSH key.
- **Release recipe (1.1.0+):** build the zip (`./build.sh release`), `git tag vX.Y.Z && git push origin vX.Y.Z`, then
  `gh release create vX.Y.Z dist/release/KikiMAIster-X.Y.Z.zip --title "KikiMAIster X.Y.Z" --notes-file <notes>`
  (notes: CHANGELOG lines, one zip for both game branches, install = unzip into `<game folder>/mods/`). Publishing is
  public: Zaille75 runs the command himself (`! ...`) or gives his explicit go.
- **Cleanup done (2026-11-02):** backup branch `backup/before-v1-scrub` and tag `backup/v0.17.7-pet-protection-immediate`
  deleted (local and GitHub).
- **1.0.2 (2026-11-02):** log's "Data folder" line fixed (empty in 1.0.1: static init order in `DataDir`); Workshop
  descriptions + README: subscribe to only one of the two items.
- **DONE in 1.1.0 (see above): one build for both game branches.** Original note: Both Workshop items share the mod id
  `KikiMAIster`: the game loads only the first one in its list, whatever the branch (the other gets DUPLICATE_ID), and
  our items declare no supported branch, so the game's branch check (`ModManager.CheckSteamBranchSupport`) lets both
  through. Plan: turn the 16 `#if GAME_DEFAULT` blocks (9 files) into runtime checks, test on both branches, then one
  Workshop item (retire the default one). Alternative: set each item's supported branch (`SetRequiredGameVersions`,
  needs a small Steamworks upload tool; SteamCMD likely can't).
- **1.0.1 (2026-11-02, released on the Workshop and GitHub; checked by Zaille75: data folder `%APPDATA%\SlayTheSpire2\KikiMAIster`):** data moved to the game's user folder (spec 03-technical "Data folder"),
  for the Workshop. `dist/tools/Envoyer-a-Claude.ps1` looks there first (then the old mods folder).
- **Steam Workshop: PUBLIC since 2026-11-02** (tested by Zaille75, data copy checked, README links). GitHub release
  v1.0.1 (tag on `d09a6b1`, the commit the zips and Workshop items were built from) published by WTT27. two items (ids in `tools/workshop/published_ids.json`: beta 3832591950, default
  3832592056, created hidden 2026-11-02), "KikiMAIster" (beta build) and "KikiMAIster (default branch)", uploaded
  by Zaille75 from his PC with SteamCMD. Package (1.1.0+): `./build.sh release &&
  python3 tools/workshop/make.py "<change note>"` → `dist/workshop/` (copied to `C:\KikiMAIster-workshop`, run
  `Publier.bat`). After the first upload, put the two ids in `tools/workshop/published_ids.json`
  (`{"beta": id, "default": id}`): later runs update the same items. Items are created hidden; Zaille75 makes them
  public on their Steam page after testing. A local copy in `mods/` with the same version disables the Workshop one.

## Open topics
1. **In-game test of v0.18.5 → 1.0.0** (the main blocker). Riskiest: the "Réglages de KikiMAIster" button in the game's
   Mods screen (its position under the description is a guess), changing a hotkey, the chifumi table, Dégâts PV / Bloqués,
   no extra death after a won run, the v0.18.9 errors (lethal at the end of the turn, En boule / Pleutre, Forme du serpent,
   Ethereal curse discarded, ally names on cards).
2. **Publishing 1.0** (see "Path to v1.0"): Steam Workshop (SteamCMD `workshop_build_item`, Zaille75 logs in), then the
   GitHub release / Nexus Mods if wanted.
3. **Maintenance after each game patch:** new DLLs in `lib/beta/` (and `lib/default/`), rebuild, PatchCheck, fix, re-sync
   `BlockAlternatives.HitCount`, bump the version.
4. **Optional, only if asked:** MVP line in the fight summary; end-of-run Totals / Per act tabs; JSON export; opacity and
   scale settings; chart history across Save & Quit.
5. **Decided, kept as is:** "Ce groupe" (compositions) differs between players (each PC reads its own run history);
   trophy names Culturiste, Peau sur les os; Intouchable only for victories.

## Path to v1.0
- **Done:** default (main) branch build (`./build.sh default`, `#if GAME_DEFAULT` blocks); icon, MIT license, README
  (English + French), CHANGELOG, manifest description; F9 reports kept in the public build (`-p:PublicRelease=true`).
- **1.0.0:** version set, tag `v1.0.0`, GitHub release with the beta and default zips. The repo goes public with it
  (checked: no game files, Steam IDs, login, server address or real e-mail in the files or the history).
- **Next:** Steam Workshop first (the game loads subscribed items itself, app 2868840; no uploader in the game folder:
  SteamCMD), then Nexus Mods if wanted. Claude checks each site's requirements; Zaille75 creates accounts / uploads where a
  login is needed.
- Zaille75 plays the **beta** branch. Since 1.1.0 the same files work on both branches.

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
  3. Run `./build.sh`. It runs the build, PatchCheck on both game branches (patches, game members used), and the unit tests. Then commit.
  4. Give Zaille75 the install command (PowerShell, game closed). The real login and server address are in
     `docs/HANDOFF.local.md` (git-ignored, this machine only):
     ```powershell
     $m = "<game folder>\mods\KikiMAIster"
     scp "<user>@<server>:<repo>/dist/KikiMAIster/*" "$m"
     ```
- The mod writes `KikiMAIster.log` next to its DLL, in the game's `mods\KikiMAIster` folder on Windows. Zaille75 copies it from there.
- **Git:** private GitHub repo `git@github.com:Zaille75/KikiMAIster.git` (`origin`, SSH key `~/.ssh/id_ed25519`).
  Commits are authored `Zaille75 <52042243+Zaille75@users.noreply.github.com>` (repo-level git config): never put
  Zaille75's real name, login or server address in tracked files.

- **Names:** talk to Zaille75 with the **French in-game names** of cards, relics, powers, potions. The table is
  `lib/beta/names.txt` (copied from the mod's `KikiMAIster.names.txt`; lines `kind|C# class|ID|name`; game text,
  kept out of git like the rest of `lib/`). Look names up there before writing to Zaille75.

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
| `Patches/UiPatches.cs` | Overlay attach (first input / combat start), hotkeys (`ModConfig.KeyPanel/KeyCompositions/KeyReport`, F7/F8/F9 by default), "Réglages de KikiMAIster" button in the game's Mods screen, hide auto summary on map |
| `UI/Overlay.cs` | Live panel: scope tabs, ◂ ▸ previous combats, sections Dégâts / PV / Défense / Malus / Erreurs de jeu / Coop / Chifumi / Feux de camp / Or / Potions et énergie / Cartes, players sorted best first, details under the player's line, hidden columns from settings |
| `UI/SettingsMenu.cs` | ⚙ window: language, dates, hotkeys, auto summary, shown sections/columns (`ModConfig.Hidden`), report button |
| `UI/CombatSummary.cs` | Draggable chart window (cumulative or per round), combat picker, co-op summary, keeps out of the panel's way, Trophées button |
| `UI/CompositionWindow.cs`, `Core/Compositions.cs` | Group compositions (F8, also in the lobby): games/wins per composition, never played / never won suggestions (`KikiMAIster.compositions.cfg`) |
| `UI/RunSummary.cs` | End-of-run trophies window, chifumi table, `RecordStore` (`KikiMAIster.records.cfg`), `ChifumiStore` |
| `Core/TurnReview.cs` | Pure end-of-turn misplay rules (TurnState → events), filled by `BlockAlternatives.Snapshot` |
| `UI/FeedbackWindow.cs` | F9 report window (zip, anonymised) |
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

## Recent version notes (code pointers)
### v0.18.10 — card-by-card review of the co-op log (2026-10-31, not tested in game)
`logs/2026-10-31_v0.18.8_beta_coop_3runs.log` (31 fights): every summary figure recomputed from the events matched (differences
by design: poison credited by stacks, Flex potion's share moved to the potion, one kill per enemy). Every v0.18.8 miss checked
against the v0.18.9 rules: wrong order 11 → 7 (Glissant / Dur à cuire removed, all real co-op ones kept with the ally's name),
Impatience ×6 and Préparatifs ×4 removed, Éventail décoratif removed, La grande manœuvre 5 HP removed; new: lethal at the end
of the turn (Putrilimace Kiki, Idole Oubliée Zaille), Forme du serpent order (Veillée T1, 8), En boule (Progéniteur, Zaille 25),
Survivant into Fléau de l'ascendant. Fixed: second `RunManager.OnEnded` after `WinRun` (`CombatFlowPatches._runOver`), free
attack / relic while asleep, `CheckBlockOnHit` consuming its pending block, `PowersCleared` applied lazily (`_cleared`).

### v0.18.9 — co-op runs of 2026-10-31 (2026-10-31, not tested in game)
Inbox `logs/inbox/2026-10-31_16-07-31/` (co-op Kiki + Zaille, 3 runs) + Zaille's reports in `logs/inbox/feedback-3/`
(archived in `logs/feedback-2026-10-31/`, logs as `logs/2026-10-31_v0.18.8_beta_coop_3runs*.log`). Rules in 01-metrics
(rows tagged v0.18.9). Code notes:
- Caps per enemy: `TurnPlay.CappedOn` (`BlockAlternatives.OnCapped(dealer, target)`); Glissant handled in
  `DebuffEffectPatches.ModifyDamage` (`slippery`, `AddEnemyCapped` with capped = 1).
- `orderTurn` args: earlier cards joined by "+", then damage, block, later cards. Another player's card: "Card:X|<id>"
  (`TurnReview.Owned`), shown by `Overlay.Arg`.
- New misses: `LethalLeft` (TurnReview → `MissedLethal`; `TurnState.LastEnemy`, `TurnPlayer.LethalMissed`),
  `discardEthereal` (Gâché, `TurnPlay.DiscardedEthereal` from `BlockAlternatives.OnCardDiscarded`), `blockOnHit` (Choix,
  emitted live by `CheckBlockOnHit`; En boule / Pleutre block from `HistoryBlock`), `orderRandom` loss kind (Forme du
  serpent), Déchaînement losses from `OnPetHpRaised`.
- `PowersCleared` event (patch on `Creature.RemoveAllPowersInternalExcept`) clears the engine's FIFO / dot ledgers.
- Gambit: `_riskyDied` (death only); reaction kill: `_enemyAttackers`.
- Hotkeys: `ModConfig.KeyPanel / KeyCompositions / KeyReport`, `SettingsMenu.TryCaptureKey`, `Loc` tokens `{KEY_…}`;
  Mods screen button: `UiPatches.ModInfoFilled` (patch on `NModInfoContainer.Fill`), `SettingsMenu.FromMods`.
- PatchCheck: beta 74, default 71. Tests: 178.

### v0.18.8 — Zaille75's answers to the review ideas (2026-10-31, not tested in game)
- A useless card with nothing else to play counts again as `energyUseless` (the v0.18.6 "no choice → surplus" rule is
  removed): a stat, never a mistake.
- New misses (TurnReview): `capCheaper` (Choix; `TurnPlay.Capped` from `BlockAlternatives.OnCapped`, called where
  `DamageCapped` is made), `stampedeSwap` (Gâché; `TurnPlayer.StampedePlayed` = attacks in hand when Ruée had a play for
  each), `drawFirst` (Choix; `TurnPlay.CardsDrawn` from `Hook.AfterCardDrawn`). Each needs a useful card the saved
  energy would have paid (`PaidBySaving`) or, for drawFirst, the reverse order possible.

### v0.18.7 — Fatalité wording + full log review (2026-10-31, not tested in game)
Zaille75: Doom is **Fatalité** in French (not Malédiction); the useless / applied x/y now replaces times / stacks in the
existing Fatalité column of Affaiblissements (`tip.doomXY`). Card-by-card review of
`logs/2026-10-31_v0.18.5_beta_solo_ironclad_necrobinder_silent.log` + `.previous.log` (Soldat de fer, Tisse-Mort,
Silencieuse ×2, ~45 fights; no internal error): **bugs fixed** — Prisme infesté's Étincelle vitale makes Skills Souillé
(`Tainted` affliction, +X damage per hit when played): such cards are now `Punished` while an enemy attacks (they gave
false "Énergie perdue" 2/1/3 and a false block kept); the full-hand rule counted Surins created by a played-again
Passe-passe (now also requires the card in `HandAtPlay`). Already handled by v0.18.6 on this log: surplus with kept
cards (Sculpteur dévoué, Tunnelier), useless block with no choice → surplus, Image rémanente / Lames fantomatiques
excused, Domaine energy, Goutte de folie useless, Rampeluche Passe-passe → 1 handFull. **Ideas left to Zaille75**
(not implemented): attack mostly stopped by a damage cap while a cheaper one did the same (Exosquelette: Frappe
perfectionnée+ capped 36; Cobaye: Frappe for 1 damage); Ruée picks a random attack, so keeping the expensive one for it
can be better; drawing (Acrobaties) before Passe-passe filled the hand (Sarcoptes, 5 Surins lost).

### v0.18.6 — solo runs of 2026-10-31 (2026-10-31, not tested in game)
Inbox `logs/inbox/2026-10-31_12-07-39/` (beta, the v0.18.5 code but labelled "v0.18.4": dist was packaged before the
version bump — **always build after bumping the version**; solo Soldat de fer, Tisse-Mort, Silencieuse ×2; 12 F9 reports,
archived as `logs/2026-10-31_v0.18.5_beta_solo_ironclad_necrobinder_silent.log` + `.previous.log`, reports in
`logs/feedback-2026-10-31/`). Answered without change: Frappe préparatoire after Frappe jumelée (it killed the last
enemy before its Strength came). Zaille75's batch (01-metrics / 02-ui v0.18.6):
- Tooltips: every column its own (new PV column **Soins**; Affaiblissements **Reçus** and **Fatalité x/y** in its existing column).
- Wrong order: `TurnReview.ResolveOrder` emits one `orderTurn` miss per earlier play (args: card, damage, block, later
  cards); `OrderLoss.Late` + `TurnPlay.PowersGiven/PowersTriggered` (`BlockAlternatives.OnPowerTriggered`, from power
  block gains and `DebuffEffectPatches.AddModifiers`) excuse later plays the earlier card's power fed.
- New misses: Ruée (`StampedePower` prefix → `TurnPlayer.StampedeShort`, "stampede"), Pas de pioche
  (`Hook.AfterPreventingDraw` → `DrawBlocked`, `EnergyGiven`, "noDraw"), full hand (`CardPileCmd.AddGeneratedCardsToCombat`
  → `LostToFullHand`, "handFull"), potions (`BlockAlternatives.UselessPotion`, "potionUseless").
- Doom: `StatsEngine` tracks Doom per enemy (`DoomApplied/DoomUseless`), `DoomPlayed` events for Doom-only cards add
  their energy to `EnergyUseless` when none of their enemies dies of Doom.
- Energy: max-energy bonuses (Domaine) emitted as `EnergyGained` at each turn start; surplus also when only kept cards
  are left; a useless card with no swappable useful card in hand counts as surplus, not `energyUseless` (Zaille75 called
  the 11:31 case a bug: a Défense against no attack with nothing else to play).
- Trophy Ce n'est qu'une égratinure: counted at the next round (or a won fight) if still at 1 HP without a heal.
- Record absolu: "value holder (date)".
Check in the next logs: `wrong order:` lines merged per card, "stampede" / "noDraw" / "handFull" / "potionUseless"
misses, `EnergyGained` with `Power:DEMESNE_POWER`.

### v0.18.5 — solo runs of 2026-10-30 (2026-10-31, not tested in game)
Inbox `logs/inbox/2026-10-31_09-51-55/` (beta v0.18.4, 5 solo runs: Régent, Soldat de fer ×2, Tisse-Mort, Défectueux;
13 F9 reports, archived as `logs/2026-10-30_v0.18.4_beta_solo_5runs.log` + `.previous.log`, reports in
`logs/feedback-2026-10-31/`). **Bug fixed:** after "Sauvegarder et quitter" + "Continuer" the mod stayed hidden (F7/F8/F9
dead) because `NPauseMenu.OnSubmenuClosed` never fires on quit; `Overlay.GameMenuOpen` now checks the menu node is
still visible. Answered without change: no lethal missed (Grouilleurs T4, 31 dmg max vs 40 HP); Hologramme+ not
"for nothing" (it brought Griffure back). Zaille75's batch (all in 02-ui v0.18.5 / 01-metrics): PV section, Défense +
Debuff column (= PV évités, in both), Erreurs de jeu, renames, Cartes last, Énergie gagnée (`PlayerCmd.GainEnergy` →
`EnergyGained` event), Énergie inutile x/y, per-column tooltips (`Overlay.Detail` joins them for the opened section),
Survolté, solo trophies (Record <perso> without tooltip, then Record absolu with the holder), value highlighted when it
beats a record, date format setting (`ModConfig.ShowDate`), Activation double/multiple/quadruple kept on purpose
(`EvokesOrbs`). **Wrong order** extended in `BlockAlternatives.CheckOrder` (Calcification via `Play.PetHits`,
Décomposition via `DexterityDrops`/`StrengthDrops`, Monologue, Rupture, Précision, Lames fantomatiques, Un pour tous,
Pistage, Bien attaché, Camouflage, Conquérant, Forge via `OnForge`, co-op Encerclement/Renverser); an enemy debuff no
longer excuses the order (`Play.OtherThanDebuff`). Zaille75 chose Forge in, "X card before energy" out. Campfire
columns only appear once an option was chosen in the run (answered, no change). Check in the next logs: `wrong order:`
lines for Calcification / Décomposition, `EnergyGained` sources, the mod visible after Save & Quit + Continue.

### v0.18.2 → v0.18.4 — solo run feedback (2026-10-30, not tested in game)
Inbox `logs/inbox/2026-10-30_15-04-14/` (beta v0.18.1, 3 solo runs: Régent, 2× Soldat de fer, 26 fights; archived as
`logs/2026-10-30_v0.18.1_beta_solo_regent_ironclad_3runs.log`, reports in `logs/feedback-2026-10-30/`). Card-by-card
check: every summary figure recomputed from the events matched (Excédent includes caps, one kill per enemy: by design).
Fixed: "Énergie pour rien" counted two block cards when only one was too much (now judged from the last back,
`TurnReview.UselessPlays`, test); false weak attack with Piétinement (`BlockAlternatives.CostWithout`); event-log lines
(Ancient choice twice with ".title", enchantment twice, shared-event line in solo). Zaille75: cards that cost HP
(Percée…) kept on purpose (`CostsHp`); section split Cartes / Potions et énergie (hidden columns migrated in
`ModConfig`); solo trophies window without winner / personal best / character names. Check in the next logs: no
`WeakAttack … STOMP` after attacks, `energyUseless` amounts, `event outcome` lines clean.

### v0.18.1 — event outcomes in the log (2026-10-30, not tested in game)
Zaille75: "track but don't introduce new stats". `RunHistoryPatches.LogEventOutcomes`: one `event outcome, floor N …`
line per player for each event / Ancient floor once complete (option, cards, relics, potions, gold, HP, Max HP), from
the map history; `shared event …` line (patch on `EventSynchronizer.ChooseOptionForSharedEvent`) with every vote and the
option kept. Check in the next logs that both lines appear and the option keys are readable. Also decided on
2026-10-30: "Ce groupe" stays per PC; trophy names Culturiste / Peau sur les os kept. The "524 runs" seen in the log
is every run on that PC (any partner, solo), not the runs with the other player: no bug.

### v0.18.0 — reports for every player (2026-10-30, not tested in game)
F9 in every build, a "Signaler un problème (F9)" button in the settings, FR/EN window, anonymised files
(`Core/Anonymizer`, tests), zip, `errors.txt`, error hint at the bottom of the panel, GitHub issue form; the DLL keeps no
local path (`PathMap`, no SourceLink). To check in game: F9 then "Enregistrer" (dev build) writes the folder + zip, and
the files show "Joueur 1/2" instead of names in co-op; the ⚠ line shows after an error and goes away after a report.
**Open before going public:** the git history still holds friends' nicknames in older commits (see the end of this
conversation's report); decide whether to rewrite it or publish a fresh history.

