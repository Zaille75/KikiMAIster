namespace KikiMAIster.Core;

/// What a potion held at death could have done before the killing blow (1.2.4, Zaille75): block, healing (Potion sanguine,
/// Régénération…), Placage, Intangible, Weak on the enemies, energy, doubling the block (Fortifiant).
public sealed record PotionView(string Key, double Block = 0, double Heal = 0, double Plating = 0, bool Intangible = false,
    bool Weak = false, int Energy = 0, bool DoublesBlock = false);

/// The player's end of their last turn: energy and block left, and the block cards kept in hand (cost, block).
public sealed record LastTurnView(int Energy, int Block, IReadOnlyList<(int Cost, int Block)> BlockCardsKept);

public static class DeathPotions
{
    /// Need: the HP the player was short of (overkill of the killing blow + 1). KillingHit: that blow's damage (blocked part
    /// included), for Weak (−25%).
    public static bool CouldSave(PotionView p, int need, int killingHit, LastTurnView? turn)
    {
        if (need <= 0) return false;
        if (p.Intangible) return true;
        if (p.Block + p.Heal + p.Plating >= need) return true;
        if (p.Weak && killingHit * 0.25 >= need) return true;
        if (turn == null) return false;
        if (p.DoublesBlock && turn.Block >= need) return true;
        return p.Energy > 0 && turn.BlockCardsKept.Any(c => c.Cost <= turn.Energy + p.Energy && c.Block >= need);
    }
}
