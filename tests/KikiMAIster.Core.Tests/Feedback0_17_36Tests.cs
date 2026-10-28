using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.36: an event's HP change before its fight, gold and curses over the run, their trophies.
public class Feedback0_17_36Tests : TestBase
{
    private static RunHistoryRead History(Actor p, double eventLost, RunEconomy? eco = null) =>
        new(p, 0, 0, new Dictionary<string, double>(), new Dictionary<string, double> { ["event"] = eventLost }, null, eco);

    [Fact]
    public void PreFightEvent_CountedOnce_AndKeptWhenHistoryIsReadAgain()
    {
        Engine.Handle(new PreFightEvent(A, "run:7", 0, 22));
        Engine.Handle(new PreFightEvent(A, "run:7", 0, 22)); // fight reloaded
        Engine.Handle(History(A, 10));
        var s = Engine.Get(Scope.Run, IdA);
        Assert.Equal(22, s.HealedOutside);
        Assert.Equal(22, s.HealedOutsideBy["event"]);
        Assert.Equal(10, s.HpLostOutside);
    }

    [Fact]
    public void Economy_FromHistory_AndTrophies()
    {
        Engine.Handle(History(A, 0, new RunEconomy(300, 250, 40, 15, 180, 50, 2, 1, 3)));
        Engine.Handle(History(B, 0, new RunEconomy(150, 20, 0, 0, 160, 130, 0, 0, 1)));
        var a = Engine.Get(Scope.Run, IdA);
        Assert.Equal(300, a.GoldGained);
        Assert.Equal(40, a.GoldLost);
        Assert.Equal(2, a.CursesGained);
        var run = new Dictionary<ulong, PlayerStats> { [IdA] = a, [IdB] = Engine.Get(Scope.Run, IdB) };
        var book = new RecordBook();
        book.Add(run, new Dictionary<ulong, string> { [IdA] = "Kiki", [IdB] = "Max" }, victory: false, "2026-10-28");
        Assert.Equal(300, book.Best("trophy.goldGained", coop: true)!.Value);
        Assert.Equal(130, book.Best("trophy.goldLeft", coop: true)!.Value);
        Assert.Equal(2, book.Best("trophy.curses", coop: true)!.Value);
    }
}

public class Feedback0_17_37Tests : TestBase
{
    [Fact]
    public void BatteriePleine_CountsSpareAndUselessEnergy()
    {
        Engine.Handle(new HelpGiven(A, "energySurplus", 2));
        Engine.Handle(new HelpGiven(A, "energyUseless", 1));
        var run = new Dictionary<ulong, PlayerStats> { [IdA] = Engine.Get(Scope.Run, IdA) };
        var book = new RecordBook();
        book.Add(run, new Dictionary<ulong, string> { [IdA] = "Kiki" }, victory: false, "2026-10-28");
        Assert.Equal(3, book.Best("trophy.energySurplus", coop: false)!.Value);
    }
}
