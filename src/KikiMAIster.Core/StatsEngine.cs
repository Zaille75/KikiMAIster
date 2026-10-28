namespace KikiMAIster.Core;

/// Turns StatEvents into per-player stats for the turn, combat and run scopes.
/// Pure logic: no game types, single-threaded (the game runs combat on its main thread).
public sealed class StatsEngine
{
    private readonly Dictionary<Scope, Dictionary<ulong, PlayerStats>> _stats = new()
    {
        [Scope.Turn] = new(), [Scope.PreviousTurn] = new(), [Scope.Combat] = new(), [Scope.Run] = new()
    };
    private readonly Dictionary<ulong, PlayerInfo> _players = new();
    private readonly Dictionary<ulong, BlockLedger> _block = new();
    private readonly Dictionary<(int CreatureId, string PowerId), DotLedger> _dots = new();
    private readonly Dictionary<(int CreatureId, string PowerId), FifoLedger> _fifo = new();
    /// Strength each player got from others' debuffs (Dominate), per player it was borrowed from.
    private readonly Dictionary<ulong, Dictionary<ulong, double>> _borrowed = new();
    private readonly Dictionary<int, bool> _hadAlternative = new();
    /// Block cards kept in hand at the end of the player turn, and the HP lost to enemies since.
    private readonly Dictionary<ulong, (string Card, int Block, double Lost)> _blockKept = new();
    /// La grande manœuvre kept this round, and the HP lost since; then, when HP were lost, the round after it: the miss
    /// only counts if no HP were lost then (v0.17.23).
    private readonly Dictionary<ulong, (string Card, int Block, double Lost)> _riskyKept = new();
    private readonly Dictionary<ulong, (string Card, int Block, double Saved, double LostAfter)> _riskyWait = new();
    /// Block cards for allies kept in hand at the end of the turn, and the HP each ally lost to enemies since.
    private readonly Dictionary<ulong, (string Card, int Block, bool All, Dictionary<ulong, double> Lost)> _allyBlockKept = new();
    /// Each player's pet HP by who gave them (Legion of Bone gives pets to allies): owner → giver → HP.
    private readonly Dictionary<ulong, Dictionary<ulong, double>> _petHp = new();
    private readonly Dictionary<int, (int Round, BlockChoice Choice)> _choices = new();
    /// Card plays in progress per player (a card can play other cards), and the damage of the outermost play so far.
    private readonly Dictionary<ulong, int> _playDepth = new();
    private readonly Dictionary<ulong, double> _playDamage = new();
    /// Damage avoided by debuffs (Weak…) on the hit about to land, per player hit: resolved by that hit (v0.17.3).
    private readonly Dictionary<ulong, List<DebuffEffect>> _pendingDefense = new();
    /// HP of a player's pet saved by debuffs (v0.17.4): only real once the pet died and its owner then loses HP in the
    /// same fight. Owner → saved effects not yet credited; owners whose pet died since.
    private readonly Dictionary<ulong, List<PetSave>> _petSaved = new();
    private readonly HashSet<ulong> _petDied = new();
    /// The same saved pet HP, for the pet's current life only (attacks that grow with the pet's HP: Déchaînement).
    private readonly Dictionary<ulong, List<PetSave>> _petSavedAlive = new();

    /// Pet HP saved by a debuff (Debuff: who was responsible, resolved at the hit) or by block (v0.17.8: Giver = who gave
    /// that block, the owner included).
    private sealed record PetSave(ulong Giver, double Amount, DebuffEffect? Debuff = null, string Source = "")
    {
        public PetSave Part(double amount) => this with { Amount = amount, Debuff = Debuff == null ? null : Debuff with { Amount = amount } };
    }

    private void AddPetSaves(ulong owner, IEnumerable<PetSave> saves)
    {
        var list = saves.ToList();
        if (!_petSaved.TryGetValue(owner, out var kept)) _petSaved[owner] = kept = new();
        kept.AddRange(list);
        if (!_petSavedAlive.TryGetValue(owner, out var alive)) _petSavedAlive[owner] = alive = new();
        alive.AddRange(list);
    }

    /// A pet save turned real: the debuff's applier gets it as HP prevented, the block's giver as block protection.
    private void CreditPetSave(ulong owner, PetSave save)
    {
        if (save.Debuff != null)
        {
            CreditResolved(save.Debuff);
            return;
        }
        Each(save.Giver, s =>
        {
            PlayerStats.Add(s.BlockStoppedBySource, save.Source, save.Amount);
            if (save.Giver == owner) s.PreventedOwnBlock += save.Amount;
            else
            {
                s.PreventedForAllies += save.Amount;
                PlayerStats.Add(s.SavedPerAlly, AllyKey(owner), save.Amount);
            }
        });
    }
    /// Rock-paper-scissors fights already counted in this run.
    private readonly HashSet<string> _chifumiFights = new();

    public int Round { get; private set; }

    private readonly List<RoundPoint> _timeline = new();
    private readonly List<CombatRecord> _combats = new();
    private int _combatNumber;
    private string _combatName = "";

    /// Finished combats of this run, oldest first.
    public IReadOnlyList<CombatRecord> Combats => _combats;

    /// The last finished combat (null before the first one ends).
    public CombatRecord? LastCombat => _combats.Count > 0 ? _combats[^1] : null;

    /// The combat being played, with a live point for the current round (null outside combat).
    public CombatRecord? CurrentCombat => InCombat
        ? new CombatRecord(_combatNumber, _combatName, null,
            _timeline.Where(p => p.Round < Round).Append(Point(Round)).ToList(), _stats[Scope.Combat])
        : null;
    public bool InCombat { get; private set; }

    /// Block absorbed or removed that no recorded layer covered (should stay 0; non-zero = capture gap).
    public double UnattributedBlock { get; private set; }

    public IReadOnlyDictionary<ulong, PlayerInfo> Players => _players;

    public IReadOnlyDictionary<ulong, PlayerStats> Get(Scope scope) => _stats[scope];

    public PlayerStats Get(Scope scope, ulong playerId) => Stats(scope, playerId);

    public void Handle(StatEvent e)
    {
        switch (e)
        {
            case RunStarted: OnRunStarted(); break;
            case CombatStarted c: OnCombatStarted(c); break;
            case RoundStarted r: OnRoundStarted(r); break;
            case CombatEnded c: OnCombatEnded(c); break;
            case DamageDealt d: OnDamage(d); break;
            case BlockGained b: OnBlockGained(b); break;
            case BlockCleared b: OnBlockCleared(b); break;
            case BlockRemoved b: OnBlockRemoved(b); break;
            case BlockObserved b: OnBlockObserved(b); break;
            case PowerChanged p: OnPowerChanged(p); break;
            case Healed h: OnHealed(h); break;
            case HpLossPrevented h: OnHpLossPrevented(h); break;
            case PetHpGiven p when p.Giver.IsPlayerSide && p.Giver.PlayerId != 0 && p.Amount > 0:
            {
                Each(p.Giver.PlayerId, s => s.PetHpGiven += p.Amount);
                var owner = p.Owner.IsPlayerSide && p.Owner.PlayerId != 0 ? p.Owner.PlayerId : p.Giver.PlayerId;
                if (!_petHp.TryGetValue(owner, out var by)) _petHp[owner] = by = new();
                by[p.Giver.PlayerId] = by.GetValueOrDefault(p.Giver.PlayerId) + p.Amount;
                break;
            }
            case DebuffEffect x: OnDebuffEffect(x); break;
            case PetHpAttack a when a.Pet.IsPlayerSide && a.Pet.PlayerId != 0: OnPetHpAttack(a); break;
            case PetHpBlock b when b.Receiver.Kind == ActorKind.Player: OnPetHpBlock(b.Receiver.PlayerId); break;
            case StrengthBorrowed b: OnStrengthBorrowed(b); break;
            case StrengthUsed u: OnStrengthUsed(u); break;
            case StrengthGiven g when g.Amount > 0 && g.Player.Kind == ActorKind.Player && g.Giver.IsPlayerSide:
            {
                if (!_borrowed.TryGetValue(g.Player.PlayerId, out var from)) _borrowed[g.Player.PlayerId] = from = new();
                from[g.Giver.PlayerId] = from.GetValueOrDefault(g.Giver.PlayerId) + g.Amount;
                break;
            }
            case MissedLethal m when m.Player.IsPlayerSide:
                Each(m.Player.PlayerId, s =>
                {
                    s.MissedLethals++;
                    s.MissedLethalTurns.Add(new MissedLethalTurn(Round, m.Cards, m.EnemiesHp, _combatNumber));
                });
                break;
            case WeakAttack w when w.Player.IsPlayerSide:
                Each(w.Player.PlayerId, s =>
                {
                    s.WeakAttacks++;
                    s.WeakAttackPlays.Add(new WeakAttackPlay(Round, w.Played, w.PlayedDamage, w.Better, w.BetterDamage, _combatNumber));
                });
                break;
            case MissedChance m when m.Player.IsPlayerSide && m.Amount > 0: AddMissed(m.Player.PlayerId, m.Kind, m.Args, m.Amount); break;
            case BlockCardKept { Risky: true } k when k.Player.IsPlayerSide && k.Block > 0: _riskyKept[k.Player.PlayerId] = (k.Card, k.Block, 0); break;
            case BlockCardKept k when k.Player.IsPlayerSide && k.Block > 0: _blockKept[k.Player.PlayerId] = (k.Card, k.Block, 0); break;
            case AllyBlockCardKept k when k.Player.IsPlayerSide && k.Block > 0:
                _allyBlockKept[k.Player.PlayerId] = (k.Card, k.Block, k.AllAllies, new());
                break;
            case CardEvent c when c.Player.IsPlayerSide:
                Each(c.Player.PlayerId, s =>
                {
                    switch (c.Action)
                    {
                        case CardAction.Drawn: s.CardsDrawn++; break;
                        case CardAction.Played: s.CardsPlayed++; break;
                        case CardAction.Exhausted: s.CardsExhausted++; break;
                        case CardAction.Discarded: s.CardsDiscarded++; break;
                    }
                });
                if (c.Action == CardAction.Played)
                {
                    // Best card play: a new count starts with each card the player chose (not with the cards it plays).
                    var depth = _playDepth.GetValueOrDefault(c.Player.PlayerId);
                    if (depth == 0) _playDamage[c.Player.PlayerId] = 0;
                    _playDepth[c.Player.PlayerId] = depth + 1;
                    // Most cards in one turn: the turn scope counts this turn's plays.
                    var thisTurn = Stats(Scope.Turn, c.Player.PlayerId).CardsPlayed;
                    Each(c.Player.PlayerId, s => s.MostCardsInTurn = Math.Max(s.MostCardsInTurn, thisTurn));
                }
                break;
            case CardPlayFinished f when f.Player.IsPlayerSide:
                _playDepth[f.Player.PlayerId] = Math.Max(0, _playDepth.GetValueOrDefault(f.Player.PlayerId) - 1);
                break;
            case HelpGiven h when h.Giver.IsPlayerSide && h.Giver.PlayerId != 0 && h.Amount > 0:
                Each(h.Giver.PlayerId, s =>
                {
                    switch (h.Kind)
                    {
                        case "energy": s.EnergyGiven += h.Amount; break;
                        case "cards": s.CardsGiven += h.Amount; break;
                        case "orbs": s.OrbsGiven += h.Amount; break;
                        case "taken": s.TakenForAllies += h.Amount; break;
                        case "energySurplus": s.EnergySurplus += h.Amount; break;
                    }
                });
                break;
            case PotionUsed p when p.Player.IsPlayerSide: Each(p.Player.PlayerId, s => s.PotionsUsed++); break;
            case PlayerDied p when p.Player.Kind == ActorKind.Player: Each(p.Player.PlayerId, s => s.Deaths++); break;
            case ChifumiFought f when f.Winner.IsPlayerSide && _chifumiFights.Add(f.FightId):
                foreach (var p in f.Players.Where(p => p.IsPlayerSide).Select(p => p.PlayerId).Distinct())
                {
                    var s = Stats(Scope.Run, p);
                    s.ChifumiPlayed++;
                    if (p == f.Winner.PlayerId) s.ChifumiWon++;
                }
                break;
            case RunHistoryRead h when h.Player.IsPlayerSide && h.Player.PlayerId != 0:
            {
                // Run scope only: the game's history covers the whole run (campfires and events happen outside combat).
                var s = Stats(Scope.Run, h.Player.PlayerId);
                s.MaxHpGained = h.MaxHpGained;
                s.MaxHpLost = h.MaxHpLost;
                s.HpLostOutsideBy.Clear();
                foreach (var (k, v) in h.HpLostOutside ?? new Dictionary<string, double>()) s.HpLostOutsideBy[k] = v;
                s.HpLostOutside = s.HpLostOutsideBy.Values.Sum();
                s.HealedOutsideBy.Clear();
                foreach (var (k, v) in h.HealedOutside ?? new Dictionary<string, double>()) s.HealedOutsideBy[k] = v;
                s.HealedOutside = s.HealedOutsideBy.Values.Sum();
                s.RestSiteChoices.Clear();
                foreach (var (k, v) in h.RestSiteChoices) s.RestSiteChoices[k] = v;
                break;
            }
            case BlockPlayResolved r:
                _hadAlternative[r.PlayId] = r.HadAlternative;
                if (r.Choice != null) _choices[r.PlayId] = (Round, r.Choice);
                break;
        }
    }

    private void OnRunStarted()
    {
        foreach (var scope in _stats.Keys.ToList()) _stats[scope] = new();
        _players.Clear();
        _combats.Clear();
        _combatNumber = 0;
        _chifumiFights.Clear();
        ResetCombatState();
    }

    /// Puts back the run totals and finished combats saved before a Save & Quit (see RunSnapshot). Call right after RunStarted.
    public void RestoreRun(IEnumerable<PlayerInfo> players, IReadOnlyDictionary<ulong, PlayerStats> runStats,
        IEnumerable<CombatRecord>? combats = null)
    {
        foreach (var p in players) _players[p.PlayerId] = p;
        foreach (var (id, s) in runStats) _stats[Scope.Run][id] = s;
        _combats.Clear();
        _combats.AddRange((combats ?? Array.Empty<CombatRecord>()).OrderBy(c => c.Number));
        _combatNumber = _combats.Count > 0 ? _combats[^1].Number : 0;
    }

    private void OnCombatStarted(CombatStarted c)
    {
        // New dictionaries: the finished combat keeps its stats in its CombatRecord.
        _stats[Scope.Turn] = new();
        _stats[Scope.PreviousTurn] = new();
        _stats[Scope.Combat] = new();
        ResetCombatState();
        InCombat = true;
        foreach (var p in c.Players)
        {
            _players[p.PlayerId] = p;
            // Make every player visible in all scopes, even with zero stats.
            foreach (var scope in _stats.Keys) Stats(scope, p.PlayerId);
        }
        _timeline.Clear();
        _combatNumber++;
        _combatName = c.Name;
        AddRoundPoint(0);
    }

    private void OnRoundStarted(RoundStarted r)
    {
        FlushDefense();
        ResolveBlockKept();
        _playDepth.Clear();
        _playDamage.Clear();
        // A new round closes the previous one on the chart.
        if (InCombat && r.Round > 1 && _timeline[^1].Round < r.Round - 1) AddRoundPoint(r.Round - 1);
        Round = r.Round;
        FreezeTurn();
    }

    /// The round that just ended becomes "previous turn"; a fresh "turn" starts.
    private void FreezeTurn()
    {
        var finished = _stats[Scope.Turn];
        _stats[Scope.Turn] = new Dictionary<ulong, PlayerStats>();
        if (finished.Count == 0) return;
        _stats[Scope.PreviousTurn] = finished;
        foreach (var id in finished.Keys) Stats(Scope.Turn, id);
    }

    private void AddRoundPoint(int round) => _timeline.Add(Point(round));

    private RoundPoint Point(int round) =>
        new(round, _stats[Scope.Combat].ToDictionary(kv => kv.Key, kv => RoundTotals.Of(kv.Value)));

    private void OnCombatEnded(CombatEnded c)
    {
        FlushDefense();
        ResolveBlockKept(combatEnded: true);
        // Block left at the end of combat counts as wasted / useless help (spec §F, decision F2).
        foreach (var (owner, ledger) in _block)
            foreach (var layer in ledger.Clear())
                CreditUnused(owner, layer, layer.Amount);
        if (InCombat)
        {
            FreezeTurn();
            if (_timeline.Count == 0 || _timeline[^1].Round < Round) AddRoundPoint(Round);
            else _timeline[^1] = Point(_timeline[^1].Round);
            _combats.Add(new CombatRecord(_combatNumber, _combatName, c.Victory, _timeline.ToList(), _stats[Scope.Combat]));
        }
        InCombat = false;
    }

    private void ResetCombatState()
    {
        Round = 0;
        _block.Clear();
        _dots.Clear();
        _fifo.Clear();
        _borrowed.Clear();
        _hadAlternative.Clear();
        _choices.Clear();
        _blockKept.Clear();
        _allyBlockKept.Clear();
        _riskyKept.Clear();
        _riskyWait.Clear();
        _petHp.Clear();
        _playDepth.Clear();
        _playDamage.Clear();
        _pendingDefense.Clear();
        _petSaved.Clear();
        _petDied.Clear();
        _petSavedAlive.Clear();
    }

    // ---------- Damage ----------

    private void OnDamage(DamageDealt d)
    {
        if (d.Target.IsEnemy) OnDamageToEnemy(d);
        else if (d.Target.IsPlayerSide) OnDamageToPlayerSide(d);
    }

    private void OnDamageToEnemy(DamageDealt d)
    {
        if (d.Source.Kind == SourceKind.DamageOverTime && d.Dealer.IsPlayerSide)
        {
            // A damage-over-time debuff kept per applier (Strangle): the capture already knows whose it is.
            CreditEnemyDamage(d.Dealer.PlayerId, d, 1, d.Source.ToString(), isDot: true);
            if (d.Killed) Each(d.Dealer.PlayerId, s => s.Kills++);
            return;
        }
        if (d.Source.Kind == SourceKind.DamageOverTime)
        {
            if (!_dots.TryGetValue((d.Target.CreatureId, d.Source.Id), out var dot)) return;
            var shares = dot.Shares().Where(s => s.ApplierId != 0).ToList();
            // Stacks from a potion (Powdered Demise…) count as potion damage.
            foreach (var (applier, origin, share) in shares)
                CreditEnemyDamage(applier, d, share, origin != "" ? origin : d.Source.ToString(), isDot: origin == "");
            // Stacks applied thanks to an ally's buff (Concoct): the damage stays the applier's, the ally enabled it.
            foreach (var (key, share) in dot.Detailed().Where(x => x.Key.Applier != 0 && x.Key.Enabler != 0))
                Each(key.Enabler, s =>
                {
                    s.DamageEnabledAllies += d.Hp * share;
                    PlayerStats.Add(s.EnabledPerAlly, AllyKey(key.Applier), d.Hp * share);
                    PlayerStats.Add(s.DebuffEffectBySource, key.EnablerSource, d.Hp * share);
                });
            if (d.Killed && shares.Count > 0)
                Each(shares.MaxBy(s => s.Share).ApplierId, s => s.Kills++);
            return;
        }
        if (!d.Dealer.IsPlayerSide) return;
        if (d.Dealer.Kind == ActorKind.Pet) CreditEnemyDamage(d.Dealer.PlayerId, d, 1, "Pet", isDot: false);
        else if (d.Source.Kind == SourceKind.Power && _dots.TryGetValue((d.Dealer.CreatureId, d.Source.Id), out var stacks))
        {
            // A power the player got from a potion (Thorns from Liquid Bronze…): that share counts as potion damage.
            var byOrigin = stacks.Shares().GroupBy(s => s.Origin).Select(g => (Origin: g.Key, Share: g.Sum(s => s.Share))).ToList();
            if (byOrigin.Count == 0) byOrigin.Add(("", 1));
            foreach (var (origin, share) in byOrigin)
                CreditEnemyDamage(d.Dealer.PlayerId, d, share, origin != "" ? origin : d.Source.ToString(), isDot: false);
        }
        else CreditEnemyDamage(d.Dealer.PlayerId, d, 1, d.Source.ToString(), isDot: false);
        if (d.Killed) Each(d.Dealer.PlayerId, s => s.Kills++);
        // Best card play: the card's hits (its pet's too), until the player chooses another card (late reports included).
        if (d.Source.Kind == SourceKind.Card && _playDamage.TryGetValue(d.Dealer.PlayerId, out var sum))
        {
            sum += d.Hp + d.Blocked + d.Overkill;
            _playDamage[d.Dealer.PlayerId] = sum;
            Each(d.Dealer.PlayerId, s => s.BestCardPlay = Math.Max(s.BestCardPlay, sum));
        }
    }

    private void CreditEnemyDamage(ulong playerId, DamageDealt d, double share, string sourceKey, bool isDot)
    {
        Each(playerId, s =>
        {
            s.DamageDealt += d.Hp * share;
            s.DamageIntoBlock += d.Blocked * share;
            s.Overkill += d.Overkill * share;
            s.BiggestHit = Math.Max(s.BiggestHit, (d.Hp + d.Blocked + d.Overkill) * share);
            PlayerStats.Add(s.DamageBySource, sourceKey, d.Hp * share);
            if (isDot) s.DotDamage += d.Hp * share;
        });
    }

    private void OnDamageToPlayerSide(DamageDealt d)
    {
        var owner = d.Target.PlayerId;
        ResolveDefense(owner, d.Blocked, d.Hp, d.Target.Kind == ActorKind.Pet);
        if (d.Target.Kind == ActorKind.Pet && d.Killed)
        {
            _petDied.Add(owner);
            _petSavedAlive.Remove(owner);
        }
        else if (d.Target.Kind == ActorKind.Player && d.Dealer.IsEnemy && d.Hp > 0 && _petDied.Contains(owner)) ReleasePetSaved(owner, d.Hp);

        // Pets use their owner's block (the game blocks pet damage with the owner's block).
        if (d.Blocked > 0)
        {
            ConsumeBlock(owner, d.Blocked, (layer, taken) =>
            {
                layer.Absorbed += taken;
                // Block an ally enabled (Sacrifice from pet HP they gave): what it stopped is HP they saved.
                foreach (var (ally, share) in layer.Enablers ?? new Dictionary<ulong, double>())
                    Each(ally, s =>
                    {
                        s.SavedByEnabledBlock += taken * share;
                        PlayerStats.Add(s.SavedPerAlly, AllyKey(owner), taken * share);
                    });
                // Block on a hit aimed at the pet saved the pet's HP: it counts like a debuff's saving (v0.17.8), once the
                // pet died and its owner loses HP, and for attacks that grow with the pet's HP.
                if (d.Target.Kind == ActorKind.Pet)
                {
                    AddPetSaves(owner, new[] { new PetSave(layer.GiverId, taken, Source: layer.Source) });
                    return;
                }
                Each(layer.GiverId, s =>
                {
                    PlayerStats.Add(s.BlockStoppedBySource, layer.Source, taken);
                    if (layer.GiverId == owner) s.PreventedOwnBlock += taken;
                    else
                    {
                        s.PreventedForAllies += taken;
                        PlayerStats.Add(s.SavedPerAlly, AllyKey(owner), taken);
                    }
                });
            });
            Each(owner, s => s.DamageBlocked += d.Blocked);
            // A pet killed by this hit: its life's savings (just added) are gone for its HP-based attacks.
            if (d.Target.Kind == ActorKind.Pet && d.Killed) _petSavedAlive.Remove(owner);
        }

        if (d.Target.Kind == ActorKind.Pet)
        {
            // Damage a pet takes instead of its owner is protection, not damage taken (Q5). The part from HP an ally
            // gave the pet is that ally's help (spec §D2).
            var by = _petHp.GetValueOrDefault(owner);
            var total = by?.Values.Sum() ?? 0;
            double own = d.Hp;
            if (by != null && total > 0)
                foreach (var (giver, hp) in by.Where(kv => kv.Key != owner))
                {
                    var part = d.Hp * hp / total;
                    own -= part;
                    Each(giver, s =>
                    {
                        s.PetAbsorbedForAllies += part;
                        PlayerStats.Add(s.SavedPerAlly, AllyKey(owner), part);
                    });
                }
            Each(owner, s =>
            {
                s.AbsorbedByPet += own;
                if (d.Killed) s.PetDeaths++;
            });
            if (d.Killed) _petHp.Remove(owner);
            return;
        }

        if (d.Dealer.IsEnemy)
        {
            Each(owner, s => s.HpLostFromEnemies += d.Hp);
            if (_blockKept.TryGetValue(owner, out var kept)) _blockKept[owner] = kept with { Lost = kept.Lost + d.Hp };
            if (_riskyKept.TryGetValue(owner, out var risky)) _riskyKept[owner] = risky with { Lost = risky.Lost + d.Hp };
            if (_riskyWait.TryGetValue(owner, out var wait)) _riskyWait[owner] = wait with { LostAfter = wait.LostAfter + d.Hp };
            foreach (var (giver, allyKept) in _allyBlockKept)
                if (giver != owner) allyKept.Lost[owner] = allyKept.Lost.GetValueOrDefault(owner) + d.Hp;
        }
        else if (d.Dealer.IsPlayerSide && d.Dealer.PlayerId == owner)
        {
            Each(owner, s => { s.HpLostFromSelf += d.Hp; s.SelfDamage += d.Hp; });
        }
        else if (d.Dealer.IsPlayerSide)
        {
            Each(owner, s => s.HpLostFromAllies += d.Hp);
            Each(d.Dealer.PlayerId, s => s.FriendlyDamage += d.Hp);
        }
        else
        {
            Each(owner, s => s.HpLostOther += d.Hp);
        }
    }

    // ---------- Block ----------

    private void OnBlockGained(BlockGained b)
    {
        if (!b.Receiver.IsPlayerSide || b.Amount <= 0) return;
        var owner = b.Receiver.PlayerId;
        var giver = b.Giver.IsPlayerSide ? b.Giver.PlayerId : owner;
        // Plating gives its block at the end of the turn, known in advance: it counts as already there, so it absorbs
        // first and a block card the Plating made unnecessary can be gâché (spec 01-metrics §F, v0.17.0).
        Ledger(owner).Add(giver, b.Amount, Round, b.PlayId, first: b.Source == Source.Power(PlatingPowerId), source: b.Source.ToString());
        Each(giver, s =>
        {
            if (giver == owner) s.BlockGainedSelf += b.Amount;
            else s.BlockGivenToAllies += b.Amount;
        });
    }

    internal const string PlatingPowerId = "PLATING_POWER";

    private void OnBlockCleared(BlockCleared b)
    {
        if (!b.Creature.IsPlayerSide) return;
        var owner = b.Creature.PlayerId;
        foreach (var layer in Ledger(owner).Clear()) CreditUnused(owner, layer, layer.Amount);
    }

    private void OnBlockObserved(BlockObserved b)
    {
        if (!b.Creature.IsPlayerSide) return;
        var owner = b.Creature.PlayerId;
        var excess = Ledger(owner).Total - b.ActualBlock;
        if (excess <= 0) return;
        foreach (var (layer, taken) in Ledger(owner).Consume(excess)) CreditUnused(owner, layer, taken);
    }

    private void OnBlockRemoved(BlockRemoved b)
    {
        if (!b.Creature.IsPlayerSide || b.Amount <= 0) return;
        var owner = b.Creature.PlayerId;
        ConsumeBlock(owner, b.Amount, (layer, taken) =>
        {
            if (b.SpentByCard) Each(layer.GiverId, s => s.BlockSpent += taken);
            else CreditUnused(owner, layer, taken);
        });
    }

    private void ConsumeBlock(ulong owner, double amount, Action<BlockLedger.Layer, double> onTaken)
    {
        // Block from a play that had an alternative absorbs last: orbs, relics, powers and the block cards that had no
        // alternative are used first, so the replaceable card is the one left unused (Givre orbs, spec F3, v0.17.32).
        var taken = Ledger(owner).Consume(amount, l => _hadAlternative.GetValueOrDefault(l.PlayId));
        foreach (var (layer, t) in taken) onTaken(layer, t);
        UnattributedBlock += amount - taken.Sum(x => x.Taken);
    }

    /// Block that never absorbed anything: wasted (own) or useless help (given by an ally).
    /// The turn scope only gets it if the block was created this round.
    private void CreditUnused(ulong owner, BlockLedger.Layer layer, double amount)
    {
        if (amount <= 0) return;
        // Wasted only when the play was resolved with a real alternative and its block stopped nothing; anything else
        // is useless (block that absorbed part of the damage was needed: its leftover is overshoot, spec F3).
        var wasted = _hadAlternative.GetValueOrDefault(layer.PlayId) && layer.Absorbed <= 0;
        var toAlly = layer.GiverId != owner;
        var hasChoice = _choices.TryGetValue(layer.PlayId, out var choice);
        Action<PlayerStats> credit = s =>
        {
            if (wasted) s.WastedBlock += amount;
            else s.UselessBlock += amount;
            if (!wasted && (layer.Absorbed > 0 || layer.PlayId == 0)) s.UnavoidableBlock += amount;
            PlayerStats.Add(s.BlockUnusedBySource, layer.Source, amount);
            if (toAlly) s.UnusedGivenToAllies += amount;
            if (wasted && hasChoice && PlayerStats.AddWasted(s.WastedPlays, layer.PlayId, layer.Round, choice.Choice, amount, _combatNumber))
                s.WastedPlayCount++;
        };
        credit(Stats(Scope.Combat, layer.GiverId));
        credit(Stats(Scope.Run, layer.GiverId));
        // Block is found unused when the next round starts: it belongs to the round it was played in.
        if (layer.Round == Round) credit(Stats(Scope.Turn, layer.GiverId));
        else if (layer.Round == Round - 1) credit(Stats(Scope.PreviousTurn, layer.GiverId));
    }

    private void OnHpLossPrevented(HpLossPrevented h)
    {
        if (h.Amount <= 0 || !h.Target.IsPlayerSide || h.Target.PlayerId == 0) return;
        Each(h.Target.PlayerId, s =>
        {
            s.PreventedByEffects += h.Amount;
            PlayerStats.Add(s.PreventedByEffectSource, h.SourceId, h.Amount);
        });
    }

    // ---------- Powers & healing ----------

    private void OnPowerChanged(PowerChanged p)
    {
        // Who applied each power's stacks, and from what origin: damage-over-time split, potion powers (Thorns…).
        var applierId = p.Applier.IsPlayerSide ? p.Applier.PlayerId : 0;
        var enabler = p.EnabledBy.IsPlayerSide && p.EnabledBy.PlayerId != applierId ? p.EnabledBy.PlayerId : 0;
        Dot(p.Target.CreatureId, p.PowerId).Apply(applierId, p.Amount, p.Origin, enabler, p.EnabledSource);
        if (p.IsDebuff) Fifo(p.Target.CreatureId, p.PowerId).Apply(applierId, p.Amount);
        if (p.Amount <= 0 || !p.IsDebuff) return;

        if (p.Applier.IsPlayerSide && p.Target.IsEnemy)
            Each(p.Applier.PlayerId, s => PlayerStats.AddPower(s.DebuffsApplied, p.PowerId, p.Amount));
        else if (p.Applier.IsPlayerSide && p.Target.IsPlayerSide && p.Applier.PlayerId == p.Target.PlayerId)
            Each(p.Applier.PlayerId, s => PlayerStats.AddPower(s.SelfDebuffs, p.PowerId, p.Amount));
        else if (p.Applier.IsPlayerSide && p.Target.IsPlayerSide)
            Each(p.Applier.PlayerId, s => PlayerStats.AddPower(s.FriendlyDebuffs, p.PowerId, p.Amount));
        else if (p.Target.IsPlayerSide && p.Target.Kind == ActorKind.Player)
            Each(p.Target.PlayerId, s => PlayerStats.AddPower(s.DebuffsReceived, p.PowerId, p.Amount));
    }

    /// Damage avoided by debuffs counts only for the HP it really saved (v0.17.3, spec §B2): what would have gone
    /// through the block without them. It is known when the hit lands: Blocked + Hp is the hit with the debuffs.
    private void ResolveDefense(ulong owner, double blocked, double hp, bool pet = false)
    {
        if (!_pendingDefense.Remove(owner, out var effects)) return;
        var avoided = effects.Sum(e => e.Amount);
        if (avoided <= 0) return;
        var blockBefore = Math.Max(blocked, _block.TryGetValue(owner, out var ledger) ? ledger.Total : 0);
        var hpWithout = Math.Max(0, blocked + hp + avoided - blockBefore);
        var saved = Math.Clamp(hpWithout - hp, 0, avoided);
        if (saved <= 0) return;
        var scaled = effects.Select(e => e with { Amount = e.Amount * saved / avoided }).ToList();
        if (pet)
        {
            // The pet's HP: kept until the pet dies and its owner loses HP (ReleasePetSaved), and while the pet lives,
            // for attacks that grow with its HP (OnPetHpAttack). Who was responsible is decided now.
            AddPetSaves(owner, scaled.SelectMany(Resolved).Select(e => new PetSave(e.Applier.PlayerId, e.Amount, e)));
            return;
        }
        foreach (var e in scaled) CreditDebuffEffect(e);
    }

    /// The pet died earlier in this fight and its owner just lost HP: without the pet HP the debuffs saved, the pet would
    /// have died sooner and the owner would have lost that much more. Credits up to the HP lost, oldest first.
    private void ReleasePetSaved(ulong owner, double hpLost)
    {
        if (!_petSaved.TryGetValue(owner, out var kept)) return;
        while (hpLost > 0 && kept.Count > 0)
        {
            var e = kept[0];
            var part = Math.Min(e.Amount, hpLost);
            CreditPetSave(owner, e.Part(part));
            hpLost -= part;
            if (part >= e.Amount) kept.RemoveAt(0);
            else kept[0] = e.Part(e.Amount - part);
        }
    }

    /// An attack that grows with the pet's HP (Déchaînement: current HP; Protecteur: max HP, v0.17.5): the part of that
    /// damage due to HP an ally gave the pet (summon, heal, max HP: same proportions as the damage the pet absorbs) or
    /// saved on it (debuffs, this life) is damage enabled ("Permis") for that ally.
    private void OnPetHpAttack(PetHpAttack a)
    {
        var owner = a.Pet.PlayerId;
        var given = _petHp.GetValueOrDefault(owner);
        var givenTotal = given?.Values.Sum() ?? 0;
        void Enable(ulong id, double amount, string source)
        {
            if (amount <= 0 || id == 0 || id == owner) return;
            Each(id, s =>
            {
                s.DamageEnabledAllies += amount;
                PlayerStats.Add(s.EnabledPerAlly, AllyKey(owner), amount);
                PlayerStats.Add(s.DebuffEffectBySource, source, amount);
            });
        }
        if (a.FromCurrentHp > 0 && a.PetHp > 0)
        {
            var saves = _petSavedAlive.GetValueOrDefault(owner) ?? new List<PetSave>();
            var savedTotal = saves.Sum(e => e.Amount);
            var scale = savedTotal > a.PetHp ? a.PetHp / savedTotal : 1;
            foreach (var e in saves) Enable(e.Giver, a.FromCurrentHp * e.Amount * scale / a.PetHp, e.Debuff?.PowerId ?? "Block");
            var rest = a.FromCurrentHp * (a.PetHp - savedTotal * scale) / a.PetHp;
            if (given != null && givenTotal > 0)
                foreach (var (giver, hp) in given) Enable(giver, rest * hp / givenTotal, "Pet");
        }
        if (a.FromMaxHp > 0 && given != null && givenTotal > 0)
            foreach (var (giver, hp) in given) Enable(giver, a.FromMaxHp * hp / givenTotal, "Pet");
    }

    /// Sacrifice: block = 3 × the pet's max HP. The allies who gave the pet HP enabled their share of it; the pet is gone.
    private void OnPetHpBlock(ulong owner)
    {
        var given = _petHp.GetValueOrDefault(owner);
        var total = given?.Values.Sum() ?? 0;
        _petHp.Remove(owner);
        if (given == null || total <= 0 || Ledger(owner).Last is not { } layer) return;
        var shares = given.Where(kv => kv.Key != owner && kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value / total);
        if (shares.Count == 0) return;
        layer.Enablers = shares;
        foreach (var (ally, share) in shares) Each(ally, s => s.BlockEnabledForAllies += layer.Amount * share);
    }

    /// Avoided damage with no hit after it (the hit was cancelled): only what the block would not have stopped.
    private void FlushDefense()
    {
        foreach (var owner in _pendingDefense.Keys.ToList()) ResolveDefense(owner, 0, 0, _pendingDefense[owner].Any(e => e.Beneficiary.Kind == ActorKind.Pet));
    }

    /// Extra damage (Vulnerable) or damage avoided (Weak, Strength down) credited to whoever is responsible for
    /// the debuff: the applier of the current turn (FIFO) or every applier by stacks (spec §B2).
    private void OnDebuffEffect(DebuffEffect x)
    {
        if (x.Amount <= 0 || !x.Beneficiary.IsPlayerSide || x.Beneficiary.PlayerId == 0) return;
        if (!x.Offense)
        {
            // Resolved by the hit that follows (ResolveDefense).
            if (!_pendingDefense.TryGetValue(x.Beneficiary.PlayerId, out var list)) _pendingDefense[x.Beneficiary.PlayerId] = list = new();
            list.Add(x);
            return;
        }
        CreditDebuffEffect(x);
    }

    private void CreditDebuffEffect(DebuffEffect x)
    {
        foreach (var direct in Resolved(x)) CreditResolved(direct);
    }

    /// The effect split between the players responsible for it now (FIFO turn, stacks…), as Direct effects: what is
    /// kept for later (pet HP saved) keeps who was responsible at the time of the hit.
    private List<DebuffEffect> Resolved(DebuffEffect x)
    {
        var shares = new List<(ulong Id, double Share)>();
        _fifo.TryGetValue((x.Holder.CreatureId, x.PowerId), out var fifo);
        switch (x.Credit)
        {
            case DebuffCredit.CurrentTurn:
                if (fifo != null && fifo.Current != 0) shares.Add((fifo.Current, 1));
                break;
            case DebuffCredit.RemainingTurns:
                if (fifo != null) shares.AddRange(fifo.Remaining().Where(r => r.ApplierId != 0));
                break;
            case DebuffCredit.Direct:
                if (x.Applier.IsPlayerSide && x.Applier.PlayerId != 0) shares.Add((x.Applier.PlayerId, 1));
                break;
            default:
            {
                var stacks = _dots.Where(kv => kv.Key.CreatureId == x.Holder.CreatureId && kv.Key.PowerId.StartsWith(x.PowerId))
                    .SelectMany(kv => kv.Value.Shares().Select(s => (s.ApplierId, Stacks: s.Share * kv.Value.Total)))
                    .GroupBy(s => s.ApplierId).Select(g => (Id: g.Key, Stacks: g.Sum(s => s.Stacks))).ToList();
                var total = stacks.Sum(s => s.Stacks);
                if (total > 0) shares.AddRange(stacks.Where(s => s.Id != 0).Select(s => (s.Id, s.Stacks / total)));
                break;
            }
        }
        return shares.Select(sh => x with { Amount = x.Amount * sh.Share, Credit = DebuffCredit.Direct, Applier = Actor.Player(sh.Id, 0) }).ToList();
    }

    private void CreditResolved(DebuffEffect x)
    {
        var beneficiary = x.Beneficiary.PlayerId;
        var id = x.Applier.PlayerId;
        if (!x.Applier.IsPlayerSide || id == 0) return;
        var amount = x.Amount;
        Each(id, s =>
        {
            if (x.Offense && id == beneficiary) s.DamageEnabledSelf += amount;
            else if (x.Offense)
            {
                s.DamageEnabledAllies += amount;
                PlayerStats.Add(s.EnabledPerAlly, AllyKey(beneficiary), amount);
            }
            else if (id == beneficiary) s.PreventedByDebuffsSelf += amount;
            else
            {
                s.PreventedByDebuffsAllies += amount;
                PlayerStats.Add(s.SavedPerAlly, AllyKey(beneficiary), amount);
            }
            PlayerStats.Add(s.DebuffEffectBySource, x.PowerId, amount);
        });
    }


    private void OnStrengthBorrowed(StrengthBorrowed b)
    {
        if (b.Amount <= 0 || b.Player.Kind != ActorKind.Player) return;
        if (!_fifo.TryGetValue((b.Holder.CreatureId, b.PowerId), out var fifo)) return;
        if (!_borrowed.TryGetValue(b.Player.PlayerId, out var from)) _borrowed[b.Player.PlayerId] = from = new();
        foreach (var (id, share) in fifo.Remaining().Where(r => r.ApplierId != 0))
            from[id] = from.GetValueOrDefault(id) + b.Amount * share;
    }

    /// The borrowed share of the Strength used in a hit is damage enabled for the players it came from (capped at the
    /// Strength the player has now: Strength lost later reduces it).
    private void OnStrengthUsed(StrengthUsed u)
    {
        if (u.Strength <= 0 || u.Damage <= 0 || !u.Player.IsPlayerSide) return;
        var from = new Dictionary<ulong, double>();
        if (_borrowed.TryGetValue(u.Player.PlayerId, out var lasting))
            foreach (var (id, points) in lasting) from[id] = points;
        foreach (var (giver, points) in u.Temporary ?? Array.Empty<(Actor, double)>())
            if (giver.IsPlayerSide && giver.PlayerId != 0) from[giver.PlayerId] = from.GetValueOrDefault(giver.PlayerId) + points;
        var borrowed = from.Values.Sum();
        if (borrowed <= 0) return;
        var scale = Math.Min(1, u.Strength / borrowed);
        foreach (var (id, amount) in from)
        {
            var extra = u.Damage * amount * scale / u.Strength;
            Each(id, s =>
            {
                if (id == u.Player.PlayerId) s.DamageEnabledSelf += extra;
                else
                {
                    s.DamageEnabledAllies += extra;
                    PlayerStats.Add(s.EnabledPerAlly, AllyKey(u.Player.PlayerId), extra);
                }
                PlayerStats.Add(s.DebuffEffectBySource, "STRENGTH_POWER", extra);
            });
        }
    }

    private void OnHealed(Healed h)
    {
        if (h.Target.Kind != ActorKind.Player || h.Amount <= 0) return;
        var target = h.Target.PlayerId;
        var healer = h.Healer.IsPlayerSide ? h.Healer.PlayerId : target;
        Each(healer, s =>
        {
            if (healer == target) s.HealingSelf += h.Amount;
            else
            {
                s.HealingAllies += h.Amount;
                PlayerStats.Add(s.SavedPerAlly, AllyKey(target), h.Amount);
            }
        });
    }

    // ---------- Missed opportunities ----------

    private void AddMissed(ulong playerId, string kind, IReadOnlyList<string> args, double amount) =>
        Each(playerId, s =>
        {
            PlayerStats.Add(s.MissedByKind, kind, amount);
            // Outside combat (campfire): no fight or round to show.
            s.MissedEntries.Add(InCombat ? new MissedEntry(Round, kind, args, amount, _combatNumber) : new MissedEntry(0, kind, args, amount));
        });

    /// A block card kept in hand while HP was then lost to enemies: the HP it could have saved (spec §E0).
    private void ResolveBlockKept(bool combatEnded = false)
    {
        // La grande manœuvre: the round after the HP loss went by without HP lost → playing it would have been safe.
        foreach (var (id, (card, block, saved, lostAfter)) in _riskyWait)
            if (lostAfter <= 0) AddMissed(id, "blockKept", new[] { card, block.ToString(System.Globalization.CultureInfo.InvariantCulture),
                saved.ToString("0", System.Globalization.CultureInfo.InvariantCulture) }, saved);
        _riskyWait.Clear();
        foreach (var (id, (card, block, lost)) in _riskyKept)
        {
            var saved = Math.Min(block, lost);
            if (saved <= 0) continue;
            // The fight is over: nothing could get through any more.
            if (combatEnded) AddMissed(id, "blockKept", new[] { card, block.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lost.ToString("0", System.Globalization.CultureInfo.InvariantCulture) }, saved);
            else _riskyWait[id] = (card, block, saved, 0);
        }
        _riskyKept.Clear();
        foreach (var (id, (card, block, lost)) in _blockKept)
        {
            var saved = Math.Min(block, lost);
            if (saved > 0) AddMissed(id, "blockKept", new[] { card, block.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lost.ToString("0", System.Globalization.CultureInfo.InvariantCulture) }, saved);
        }
        _blockKept.Clear();
        foreach (var (id, (card, block, all, lost)) in _allyBlockKept)
        {
            var saved = all ? lost.Values.Sum(l => Math.Min(block, l)) : lost.Values.Select(l => Math.Min(block, l)).DefaultIfEmpty(0).Max();
            if (saved > 0) AddMissed(id, "allyBlockKept", new[] { card, block.ToString(System.Globalization.CultureInfo.InvariantCulture),
                saved.ToString("0", System.Globalization.CultureInfo.InvariantCulture) }, saved);
        }
        _allyBlockKept.Clear();
    }

    // ---------- Helpers ----------

    private static string AllyKey(ulong playerId) => playerId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Each(ulong playerId, Action<PlayerStats> update)
    {
        // Outside combat (campfire, events, map) only the run counts: the combat scope still holds the last finished
        // combat (v0.17.14).
        if (!InCombat)
        {
            update(Stats(Scope.Run, playerId));
            return;
        }
        // Live events never go to the frozen previous turn.
        foreach (var scope in _stats.Keys)
            if (scope != Scope.PreviousTurn) update(Stats(scope, playerId));
    }

    private PlayerStats Stats(Scope scope, ulong playerId)
    {
        var map = _stats[scope];
        if (!map.TryGetValue(playerId, out var s)) map[playerId] = s = new PlayerStats();
        return s;
    }

    private BlockLedger Ledger(ulong owner)
    {
        if (!_block.TryGetValue(owner, out var l)) _block[owner] = l = new BlockLedger();
        return l;
    }

    private FifoLedger Fifo(int creatureId, string powerId)
    {
        if (!_fifo.TryGetValue((creatureId, powerId), out var l)) _fifo[(creatureId, powerId)] = l = new FifoLedger();
        return l;
    }

    private DotLedger Dot(int creatureId, string powerId)
    {
        if (!_dots.TryGetValue((creatureId, powerId), out var l)) _dots[(creatureId, powerId)] = l = new DotLedger();
        return l;
    }
}
