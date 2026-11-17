namespace KikiMAIster.Core;

/// "Front hand" lethal check (spec 01-metrics §E): could the attacks in hand, within the energy available, kill
/// every enemy? Only the game's damage preview of real cards is used, so it is a minimum (draw, random effects and
/// combos are ignored).
public static class Lethal
{
    /// Damage: previewed damage of this attack against each enemy (same order as the needs). Shot: one of the Fool's Shots,
    /// which needs an arrow ready (1.2.1).
    public sealed record Attack(string Id, int Cost, bool AllEnemies, IReadOnlyList<int> Damage, bool Shot = false);

    private const int MaxCards = 12;

    /// Needs: HP + block of each enemy. Returns the attacks of a lethal combination (fewest energy), or null.
    /// MaxPlays: the most cards the player may still play this turn (Paresse, v0.17.38). MaxShots: the Fool's arrows ready
    /// (1.2.1: a Shot that kills gives its arrow back, ignored, so the check stays a minimum).
    /// DeathBlock (1.2.3, Crabe furieux): block each enemy gains when another enemy dies; the one killed last gets it.
    public static IReadOnlyList<Attack>? Find(IReadOnlyList<Attack> attacks, int energy, IReadOnlyList<int> needs, int maxPlays = int.MaxValue,
        int maxShots = int.MaxValue, IReadOnlyList<int>? deathBlock = null)
    {
        if (needs.Count == 0) return null;
        var cards = attacks.Where(a => a.Cost <= energy && a.Damage.Count == needs.Count && a.Damage.Any(d => d > 0))
            .Take(MaxCards).ToList();
        IReadOnlyList<Attack>? best = null;
        var bestCost = int.MaxValue;
        for (var mask = 1; mask < 1 << cards.Count; mask++)
        {
            var chosen = cards.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            if (chosen.Count > maxPlays || chosen.Count(a => a.Shot) > maxShots) continue;
            var cost = chosen.Sum(a => a.Cost);
            if (cost > energy || cost >= bestCost) continue;
            if (!KillsAll(chosen, needs, deathBlock)) continue;
            best = chosen;
            bestCost = cost;
        }
        return best;
    }

    /// With a death bonus, the best order is the one where the enemy that gains it dies last: one try per such enemy.
    private static bool KillsAll(List<Attack> chosen, IReadOnlyList<int> needs, IReadOnlyList<int>? deathBlock)
    {
        if (deathBlock == null || needs.Count < 2 || deathBlock.All(b => b <= 0)) return Kills(chosen, needs);
        for (var i = 0; i < needs.Count; i++)
        {
            if (deathBlock[i] <= 0) continue;
            var withBlock = needs.ToArray();
            withBlock[i] += deathBlock[i];
            if (Kills(chosen, withBlock)) return true;
        }
        return false;
    }

    /// Attacks on all enemies hit everyone; each single-target attack goes, biggest first, to an enemy it can
    /// finish (the toughest such enemy), else to the enemy closest to dying.
    private static bool Kills(List<Attack> chosen, IReadOnlyList<int> needs)
    {
        var left = needs.ToArray();
        foreach (var a in chosen.Where(a => a.AllEnemies))
            for (var e = 0; e < left.Length; e++) left[e] -= a.Damage[e];
        foreach (var a in chosen.Where(a => !a.AllEnemies).OrderByDescending(a => a.Damage.Max()))
        {
            var alive = Enumerable.Range(0, left.Length).Where(e => left[e] > 0).ToList();
            if (alive.Count == 0) break;
            var finish = alive.Where(e => a.Damage[e] >= left[e]).OrderByDescending(e => left[e]).ToList();
            var target = finish.Count > 0 ? finish[0] : alive.OrderBy(e => left[e]).First();
            left[target] -= a.Damage[target];
        }
        return left.All(l => l <= 0);
    }
}
