using System.Globalization;
using KikiMAIster.Core;

namespace KikiMAIster;

/// Text summary in the log until the in-game UI exists (Phase 3).
public static class Summary
{
    public static void LogCombat(StatsEngine engine)
    {
        ModLog.Info("===== COMBAT SUMMARY =====");
        foreach (var (id, s) in engine.Get(Scope.Combat))
        {
            var name = engine.Players.TryGetValue(id, out var p) ? $"{p.Name} ({p.Character})" : id.ToString();
            ModLog.Info($"--- {name}");
            ModLog.Info($"  Damage: dealt={F(s.DamageDealt)} intoBlock={F(s.DamageIntoBlock)} overkill={F(s.Overkill)} " +
                        $"kills={F(s.Kills)} biggestHit={F(s.BiggestHit)} dot={F(s.DotDamage)}");
            ModLog.Info($"  By source: {Map(s.DamageBySource)}");
            ModLog.Info($"  Debuffs applied: {Powers(s.DebuffsApplied)}");
            ModLog.Info($"  Protection: blockSelf={F(s.BlockGainedSelf)} blockToAllies={F(s.BlockGivenToAllies)} " +
                        $"preventedOwn={F(s.PreventedOwnBlock)} preventedAllies={F(s.PreventedForAllies)} " +
                        $"wasted={F(s.WastedBlock)} useless={F(s.UselessBlock)} unusedToAllies={F(s.UnusedGivenToAllies)} spent={F(s.BlockSpent)} " +
                        $"pet={F(s.AbsorbedByPet)} byEffects={F(s.PreventedByEffects)} petDeaths={F(s.PetDeaths)} healSelf={F(s.HealingSelf)} healAllies={F(s.HealingAllies)}");
            ModLog.Info($"  Taken: hpLost={F(s.HpLost)} (enemies={F(s.HpLostFromEnemies)} allies={F(s.HpLostFromAllies)} " +
                        $"self={F(s.HpLostFromSelf)} other={F(s.HpLostOther)}) blocked={F(s.DamageBlocked)} debuffs={Powers(s.DebuffsReceived)}");
            ModLog.Info($"  Friendly fire: damage={F(s.FriendlyDamage)} self={F(s.SelfDamage)} debuffs={Powers(s.FriendlyDebuffs)}");
        }
        if (engine.UnattributedBlock > 0) ModLog.Info($"  (unattributed block: {F(engine.UnattributedBlock)})");
    }

    private static string F(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

    private static string Map(Dictionary<string, double> m) =>
        m.Count == 0 ? "-" : string.Join(", ", m.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={F(kv.Value)}"));

    private static string Powers(Dictionary<string, PowerTally> m) =>
        m.Count == 0 ? "-" : string.Join(", ", m.Select(kv => $"{kv.Key} x{kv.Value.Count} ({F(kv.Value.Stacks)})"));
}
