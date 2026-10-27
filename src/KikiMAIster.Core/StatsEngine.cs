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
    private readonly Dictionary<int, bool> _hadAlternative = new();
    private readonly Dictionary<int, (int Round, BlockChoice Choice)> _choices = new();

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
            _timeline.Where(p => p.Round < Round).Append(Point(Round)).ToList())
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
                Each(p.Giver.PlayerId, s => s.PetHpGiven += p.Amount);
                break;
            case BlockPlayResolved r:
                _hadAlternative[r.PlayId] = r.HadAlternative;
                if (r.Choice != null) _choices[r.PlayId] = (Round, r.Choice);
                break;
        }
    }

    private void OnRunStarted()
    {
        foreach (var s in _stats.Values) s.Clear();
        _players.Clear();
        _combats.Clear();
        _combatNumber = 0;
        ResetCombatState();
    }

    /// Puts back the run totals saved before a Save & Quit (see RunSnapshot). Call right after RunStarted.
    public void RestoreRun(IEnumerable<PlayerInfo> players, IReadOnlyDictionary<ulong, PlayerStats> runStats)
    {
        foreach (var p in players) _players[p.PlayerId] = p;
        foreach (var (id, s) in runStats) _stats[Scope.Run][id] = s;
    }

    private void OnCombatStarted(CombatStarted c)
    {
        _stats[Scope.Turn].Clear();
        _stats[Scope.PreviousTurn].Clear();
        _stats[Scope.Combat].Clear();
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
        // Block left at the end of combat counts as wasted / useless help (spec §F, decision F2).
        foreach (var (owner, ledger) in _block)
            foreach (var layer in ledger.Clear())
                CreditUnused(owner, layer, layer.Amount);
        if (InCombat)
        {
            FreezeTurn();
            if (_timeline.Count == 0 || _timeline[^1].Round < Round) AddRoundPoint(Round);
            else _timeline[^1] = Point(_timeline[^1].Round);
            _combats.Add(new CombatRecord(_combatNumber, _combatName, c.Victory, _timeline.ToList()));
        }
        InCombat = false;
    }

    private void ResetCombatState()
    {
        Round = 0;
        _block.Clear();
        _dots.Clear();
        _hadAlternative.Clear();
        _choices.Clear();
    }

    // ---------- Damage ----------

    private void OnDamage(DamageDealt d)
    {
        if (d.Target.IsEnemy) OnDamageToEnemy(d);
        else if (d.Target.IsPlayerSide) OnDamageToPlayerSide(d);
    }

    private void OnDamageToEnemy(DamageDealt d)
    {
        if (d.Source.Kind == SourceKind.DamageOverTime)
        {
            if (!_dots.TryGetValue((d.Target.CreatureId, d.Source.Id), out var dot)) return;
            var shares = dot.Shares().Where(s => s.ApplierId != 0).ToList();
            // Stacks from a potion (Powdered Demise…) count as potion damage.
            foreach (var (applier, origin, share) in shares)
                CreditEnemyDamage(applier, d, share, origin != "" ? origin : d.Source.ToString(), isDot: origin == "");
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

        // Pets use their owner's block (the game blocks pet damage with the owner's block).
        if (d.Blocked > 0)
        {
            ConsumeBlock(owner, d.Blocked, (layer, taken) =>
                Each(layer.GiverId, s =>
                {
                    if (layer.GiverId == owner) s.PreventedOwnBlock += taken;
                    else s.PreventedForAllies += taken;
                }));
            Each(owner, s => s.DamageBlocked += d.Blocked);
        }

        if (d.Target.Kind == ActorKind.Pet)
        {
            // Damage a pet takes instead of its owner is protection, not damage taken (Q5).
            Each(owner, s =>
            {
                s.AbsorbedByPet += d.Hp;
                if (d.Killed) s.PetDeaths++;
            });
            return;
        }

        if (d.Dealer.IsEnemy)
        {
            Each(owner, s => s.HpLostFromEnemies += d.Hp);
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
        Ledger(owner).Add(giver, b.Amount, Round, b.PlayId);
        Each(giver, s =>
        {
            if (giver == owner) s.BlockGainedSelf += b.Amount;
            else s.BlockGivenToAllies += b.Amount;
        });
    }

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
        var taken = Ledger(owner).Consume(amount);
        foreach (var (layer, t) in taken) onTaken(layer, t);
        UnattributedBlock += amount - taken.Sum(x => x.Taken);
    }

    /// Block that never absorbed anything: wasted (own) or useless help (given by an ally).
    /// The turn scope only gets it if the block was created this round.
    private void CreditUnused(ulong owner, BlockLedger.Layer layer, double amount)
    {
        if (amount <= 0) return;
        // Wasted only when the play was resolved with a real alternative; anything else is useless.
        var wasted = _hadAlternative.GetValueOrDefault(layer.PlayId);
        var toAlly = layer.GiverId != owner;
        var hasChoice = _choices.TryGetValue(layer.PlayId, out var choice);
        Action<PlayerStats> credit = s =>
        {
            if (wasted) s.WastedBlock += amount;
            else s.UselessBlock += amount;
            if (toAlly) s.UnusedGivenToAllies += amount;
            if (wasted && hasChoice) PlayerStats.AddWasted(s.WastedPlays, layer.PlayId, layer.Round, choice.Choice, amount);
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
        Dot(p.Target.CreatureId, p.PowerId).Apply(applierId, p.Amount, p.Origin);
        if (p.Amount <= 0 || !p.IsDebuff) return;

        if (p.Applier.IsPlayerSide && p.Target.IsEnemy)
            Each(p.Applier.PlayerId, s => PlayerStats.AddPower(s.DebuffsApplied, p.PowerId, p.Amount));
        else if (p.Applier.IsPlayerSide && p.Target.IsPlayerSide)
            Each(p.Applier.PlayerId, s => PlayerStats.AddPower(s.FriendlyDebuffs, p.PowerId, p.Amount));
        else if (p.Target.IsPlayerSide && p.Target.Kind == ActorKind.Player)
            Each(p.Target.PlayerId, s => PlayerStats.AddPower(s.DebuffsReceived, p.PowerId, p.Amount));
    }

    private void OnHealed(Healed h)
    {
        if (h.Target.Kind != ActorKind.Player || h.Amount <= 0) return;
        var target = h.Target.PlayerId;
        var healer = h.Healer.IsPlayerSide ? h.Healer.PlayerId : target;
        Each(healer, s =>
        {
            if (healer == target) s.HealingSelf += h.Amount;
            else s.HealingAllies += h.Amount;
        });
    }

    // ---------- Helpers ----------

    private void Each(ulong playerId, Action<PlayerStats> update)
    {
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

    private DotLedger Dot(int creatureId, string powerId)
    {
        if (!_dots.TryGetValue((creatureId, powerId), out var l)) _dots[(creatureId, powerId)] = l = new DotLedger();
        return l;
    }
}
