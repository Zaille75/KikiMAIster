using KikiMAIster.Core;
using Xunit;

namespace KikiMAIster.Core.Tests;

/// v0.17.34: unavoidable useless block, HP lost outside fights, Stars and Carapace endurcie misses.
public class Feedback0_17_34Tests : TestBase
{
    [Fact]
    public void UselessBlock_LeftoverOfNeededBlock_IsUnavoidable()
    {
        // 5 block for a 1-damage hit: 4 left over, unavoidable. A relic's 3 block unused: unavoidable too.
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 1));
        Engine.Handle(new BlockPlayResolved(1, false));
        Engine.Handle(new BlockGained(A, A, 3, new Source(SourceKind.Relic, "ANCHOR")));
        EnemyHits(A, 1, 0);
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockObserved(A, 0));
        Assert.Equal(7, Combat(IdA).UselessBlock);
        Assert.Equal(7, Combat(IdA).UnavoidableBlock);
    }

    [Fact]
    public void UselessBlock_UnusedCard_IsAvoidable()
    {
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 1));
        Engine.Handle(new BlockPlayResolved(1, false));
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockObserved(A, 0));
        Assert.Equal(5, Combat(IdA).UselessBlock);
        Assert.Equal(0, Combat(IdA).UnavoidableBlock);
    }

    [Fact]
    public void HpLostOutside_FromRunHistory_RunScopeOnly()
    {
        Engine.Handle(new RunHistoryRead(A, 0, 25, new Dictionary<string, double>(), HpLostOutside: 12));
        Assert.Equal(12, Engine.Get(Scope.Run, IdA).HpLostOutside);
        Assert.Equal(0, Combat(IdA).HpLostOutside);
    }

    [Fact]
    public void Stars_WastedInGache_ReplaceableInChoix()
    {
        Engine.Handle(new MissedChance(A, "starsWasted", new[] { "Card:CLOAK_OF_STARS", "3" }));
        Engine.Handle(new MissedChance(A, "stars", new[] { "Card:CLOAK_OF_STARS", "3", "Card:DEFEND" }));
        Engine.Handle(new MissedChance(A, "shell", new[] { "Card:ANGER", "Power:HARDENED_SHELL_POWER", "Card:DEFEND" }));
        Assert.Equal(1, Combat(IdA).MissedWasted);
        Assert.Equal(2, Combat(IdA).MissedChoices);
    }
}
