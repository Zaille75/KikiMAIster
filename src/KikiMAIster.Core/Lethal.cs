namespace KikiMAIster.Core;

/// "Front hand" lethal check (spec 01-metrics §E): could the attacks in hand, within the energy available, kill
/// every enemy? Only the game's damage preview of real cards is used, so it is a minimum (draw, random effects and
/// combos are ignored).
public static class Lethal
{
    /// Damage: previewed damage of this attack against each enemy (same order as the needs).
    public sealed record Attack(string Id, int Cost, bool AllEnemies, IReadOnlyList<int> Damage);

    private const int MaxCards = 12;

    /// Needs: HP + block of each enemy. Returns the attacks of a lethal combination (fewest energy), or null.
    /// MaxPlays: the most cards the player may still play this turn (Paresse, v0.17.38).
    public static IReadOnlyList<Attack>? Find(IReadOnlyList<Attack> attacks, int energy, IReadOnlyList<int> needs, int maxPlays = int.MaxValue)
    {
        if (needs.Count == 0) return null;
        var cards = attacks.Where(a => a.Cost <= energy && a.Damage.Count == needs.Count && a.Damage.Any(d => d > 0))
            .Take(MaxCards).ToList();
        IReadOnlyList<Attack>? best = null;
        var bestCost = int.MaxValue;
        for (var mask = 1; mask < 1 << cards.Count; mask++)
        {
            var chosen = cards.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            if (chosen.Count > maxPlays) continue;
            var cost = chosen.Sum(a => a.Cost);
            if (cost > energy || cost >= bestCost) continue;
            if (!Kills(chosen, needs)) continue;
            best = chosen;
            bestCost = cost;
        }
        return best;
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
