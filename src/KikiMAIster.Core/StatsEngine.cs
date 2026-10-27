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
                Each(p.Giver.PlayerId, s => s.PetHpGiven += p.Amount);
                break;
            case DebuffEffect x: OnDebuffEffect(x); break;
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
                    s.MissedLethalTurns.Add(new MissedLethalTurn(Round, m.Cards, m.EnemiesHp));
                });
                break;
            case WeakAttack w when w.Player.IsPlayerSide:
                Each(w.Player.PlayerId, s =>
                {
                    s.WeakAttacks++;
                    s.WeakAttackPlays.Add(new WeakAttackPlay(Round, w.Played, w.PlayedDamage, w.Better, w.BetterDamage));
                });
                break;
            case MissedChance m when m.Player.IsPlayerSide && m.Amount > 0: AddMissed(m.Player.PlayerId, m.Kind, m.Args, m.Amount); break;
            case BlockCardKept k when k.Player.IsPlayerSide && k.Block > 0: _blockKept[k.Player.PlayerId] = (k.Card, k.Block, 0); break;
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
                    // Most cards in one turn: the turn scope counts this turn's plays.
                    var thisTurn = Stats(Scope.Turn, c.Player.PlayerId).CardsPlayed;
                    Each(c.Player.PlayerId, s => s.MostCardsInTurn = Math.Max(s.MostCardsInTurn, thisTurn));
                }
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
                    }
                });
                break;
            case PotionUsed p when p.Player.IsPlayerSide: Each(p.Player.PlayerId, s => s.PotionsUsed++); break;
            case PlayerDied p when p.Player.Kind == ActorKind.Player: Each(p.Player.PlayerId, s => s.Deaths++); break;
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
        ResolveBlockKept();
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
        ResolveBlockKept();
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
            if (_blockKept.TryGetValue(owner, out var kept)) _blockKept[owner] = kept with { Lost = kept.Lost + d.Hp };
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
            if (wasted && hasChoice && PlayerStats.AddWasted(s.WastedPlays, layer.PlayId, layer.Round, choice.Choice, amount))
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
        else if (p.Applier.IsPlayerSide && p.Target.IsPlayerSide)
            Each(p.Applier.PlayerId, s => PlayerStats.AddPower(s.FriendlyDebuffs, p.PowerId, p.Amount));
        else if (p.Target.IsPlayerSide && p.Target.Kind == ActorKind.Player)
            Each(p.Target.PlayerId, s => PlayerStats.AddPower(s.DebuffsReceived, p.PowerId, p.Amount));
    }

    /// Extra damage (Vulnerable) or damage avoided (Weak, Strength down) credited to whoever is responsible for
    /// the debuff: the applier of the current turn (FIFO) or every applier by stacks (spec §B2).
    private void OnDebuffEffect(DebuffEffect x)
    {
        if (x.Amount <= 0 || !x.Beneficiary.IsPlayerSide || x.Beneficiary.PlayerId == 0) return;
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
        var beneficiary = x.Beneficiary.PlayerId;
        foreach (var (id, share) in shares)
        {
            var amount = x.Amount * share;
            Each(id, s =>
            {
                if (x.Offense && id == beneficiary) s.DamageEnabledSelf += amount;
                else if (x.Offense) s.DamageEnabledAllies += amount;
                else if (id == beneficiary) s.PreventedByDebuffsSelf += amount;
                else s.PreventedByDebuffsAllies += amount;
                PlayerStats.Add(s.DebuffEffectBySource, x.PowerId, amount);
            });
        }
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
                else s.DamageEnabledAllies += extra;
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
            else s.HealingAllies += h.Amount;
        });
    }

    // ---------- Missed opportunities ----------

    private void AddMissed(ulong playerId, string kind, IReadOnlyList<string> args, double amount) =>
        Each(playerId, s =>
        {
            PlayerStats.Add(s.MissedByKind, kind, amount);
            s.MissedEntries.Add(new MissedEntry(Round, kind, args, amount));
        });

    /// A block card kept in hand while HP was then lost to enemies: the HP it could have saved (spec §E0).
    private void ResolveBlockKept()
    {
        foreach (var (id, (card, block, lost)) in _blockKept)
        {
            var saved = Math.Min(block, lost);
            if (saved > 0) AddMissed(id, "blockKept", new[] { card, block.ToString(System.Globalization.CultureInfo.InvariantCulture),
                lost.ToString("0", System.Globalization.CultureInfo.InvariantCulture) }, saved);
        }
        _blockKept.Clear();
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
