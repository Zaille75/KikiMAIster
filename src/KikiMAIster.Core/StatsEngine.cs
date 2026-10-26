namespace KikiMAIster.Core;

/// Turns StatEvents into per-player stats for the turn, combat and run scopes.
/// Pure logic: no game types, single-threaded (the game runs combat on its main thread).
public sealed class StatsEngine
{
    private readonly Dictionary<Scope, Dictionary<ulong, PlayerStats>> _stats = new()
    {
        [Scope.Turn] = new(), [Scope.Combat] = new(), [Scope.Run] = new()
    };
    private readonly Dictionary<ulong, PlayerInfo> _players = new();
    private readonly Dictionary<ulong, BlockLedger> _block = new();
    private readonly Dictionary<(int CreatureId, string PowerId), DotLedger> _dots = new();

    public int Round { get; private set; }
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
            case RoundStarted r: Round = r.Round; _stats[Scope.Turn].Clear(); break;
            case CombatEnded: OnCombatEnded(); break;
            case DamageDealt d: OnDamage(d); break;
            case BlockGained b: OnBlockGained(b); break;
            case BlockCleared b: OnBlockCleared(b); break;
            case BlockRemoved b: OnBlockRemoved(b); break;
            case PowerChanged p: OnPowerChanged(p); break;
            case Healed h: OnHealed(h); break;
        }
    }

    private void OnRunStarted()
    {
        foreach (var s in _stats.Values) s.Clear();
        _players.Clear();
        ResetCombatState();
    }

    private void OnCombatStarted(CombatStarted c)
    {
        _stats[Scope.Turn].Clear();
        _stats[Scope.Combat].Clear();
        ResetCombatState();
        InCombat = true;
        foreach (var p in c.Players)
        {
            _players[p.PlayerId] = p;
            // Make every player visible in all scopes, even with zero stats.
            foreach (var scope in _stats.Keys) Stats(scope, p.PlayerId);
        }
    }

    private void OnCombatEnded()
    {
        // Block left at the end of combat counts as wasted / useless help (spec §F, decision F2).
        foreach (var (owner, ledger) in _block)
            foreach (var layer in ledger.Clear())
                CreditUnused(owner, layer, layer.Amount);
        InCombat = false;
    }

    private void ResetCombatState()
    {
        Round = 0;
        _block.Clear();
        _dots.Clear();
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
            foreach (var (applier, share) in shares)
                CreditEnemyDamage(applier, d, share, d.Source.ToString(), isDot: true);
            if (d.Killed && shares.Count > 0)
                Each(shares.MaxBy(s => s.Share).ApplierId, s => s.Kills++);
            return;
        }
        if (!d.Dealer.IsPlayerSide) return;
        var key = d.Dealer.Kind == ActorKind.Pet ? "Pet" : d.Source.ToString();
        CreditEnemyDamage(d.Dealer.PlayerId, d, 1, key, isDot: false);
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
        Ledger(owner).Add(giver, b.Amount, Round);
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
        Action<PlayerStats> credit = layer.GiverId == owner
            ? s => s.WastedBlock += amount
            : s => s.UselessHelp += amount;
        credit(Stats(Scope.Combat, layer.GiverId));
        credit(Stats(Scope.Run, layer.GiverId));
        if (layer.Round == Round) credit(Stats(Scope.Turn, layer.GiverId));
    }

    // ---------- Powers & healing ----------

    private void OnPowerChanged(PowerChanged p)
    {
        if (p.IsDebuff)
        {
            var applierId = p.Applier.IsPlayerSide ? p.Applier.PlayerId : 0;
            Dot(p.Target.CreatureId, p.PowerId).Apply(applierId, p.Amount);
        }
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
        foreach (var scope in _stats.Keys) update(Stats(scope, playerId));
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
