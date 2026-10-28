using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

/// v0.17.0: chifumi, Max HP and campfire stats (run scope), Plating block first, composition order and group filter.
public class RunHistoryTests : TestBase
{
    [Fact]
    public void Chifumi_CountedOncePerFight_RunScopeOnly()
    {
        Engine.Handle(new ChifumiFought("f1", A, new[] { A, B }));
        Engine.Handle(new ChifumiFought("f1", A, new[] { A, B })); // reloaded treasure room
        Engine.Handle(new ChifumiFought("f2", B, new[] { A, B, C }));
        var a = Engine.Get(Scope.Run, IdA);
        Assert.Equal(1, a.ChifumiWon);
        Assert.Equal(2, a.ChifumiPlayed);
        Assert.Equal(1, Engine.Get(Scope.Run, IdC).ChifumiPlayed);
        Assert.Equal(0, Combat(IdA).ChifumiPlayed);
    }

    [Fact]
    public void ChifumiBook_HeadToHead_SurvivesRoundTrip()
    {
        var book = new Chifumi();
        Assert.True(book.Record("f1", 2, new ulong[] { 1, 2 }, new Dictionary<ulong, string> { [1] = "Kiki", [2] = "Max" }));
        Assert.False(book.Record("f1", 2, new ulong[] { 1, 2 }));
        book.Record("f2", 1, new ulong[] { 1, 2, 3 });
        book = Chifumi.Read(book.Write());
        Assert.Equal((1, 1), book.Against(1, 2));
        Assert.Equal((1, 1), book.Against(2, 1));
        Assert.Equal((1, 0), book.Against(1, 3));
        Assert.Equal((0, 1), book.Against(3, 1));
        Assert.Equal("Max", book.NameOf(2));
        Assert.False(book.Record("f2", 1, new ulong[] { 1, 2 }));
    }

    [Fact]
    public void RunHistory_ReplacesValues()
    {
        Engine.Handle(new RunHistoryRead(A, 7, 3, new Dictionary<string, double> { ["HEAL"] = 2 }));
        Engine.Handle(new RunHistoryRead(A, 10, 3, new Dictionary<string, double> { ["HEAL"] = 2, ["SMITH"] = 1 }));
        var a = Engine.Get(Scope.Run, IdA);
        Assert.Equal(10, a.MaxHpGained);
        Assert.Equal(3, a.MaxHpLost);
        Assert.Equal(1, a.RestSiteChoices["SMITH"]);
        Assert.Equal(0, Combat(IdA).MaxHpGained);
        var snapshot = RunSnapshot.Read(RunSnapshot.Write("k", 3, Engine))!;
        Assert.Equal(2, snapshot.Stats[IdA].RestSiteChoices["HEAL"]);
    }

    [Fact]
    public void Plating_AbsorbsFirst_SoTheBlockCardIsWasted()
    {
        Engine.Handle(new BlockGained(A, A, 5, Source.Card("DEFEND"), PlayId: 1));
        Engine.Handle(new BlockPlayResolved(1, true, new BlockChoice("Card:DEFEND", "Card:STRIKE", new[] { "Card:STRIKE" }, 10, 5)));
        Engine.Handle(new BlockGained(A, A, 5, Source.Power("PLATING_POWER")));
        EnemyHits(A, 5, 0);
        Engine.Handle(new RoundStarted(2));
        Engine.Handle(new BlockObserved(A, 0));
        Assert.Equal(5, Combat(IdA).WastedBlock);
        Assert.Equal(1, Combat(IdA).WastedPlayCount);
    }

    [Fact]
    public void MaxHpLost_VictoryOnly()
    {
        var run = new Dictionary<ulong, PlayerStats> { [1] = new() { MaxHpLost = 8 }, [2] = new() { MaxHpGained = 5 } };
        var names = new Dictionary<ulong, string> { [1] = "Kiki", [2] = "Max" };
        var book = new RecordBook();
        book.Add(run, names, victory: false, "2026-10-28");
        Assert.Null(book.Best("trophy.maxHpLost", coop: true));
        Assert.Equal(5, book.Best("trophy.maxHpGained", coop: true)!.Value);
        book.Add(run, names, victory: true, "2026-10-28");
        Assert.Equal(8, book.Best("trophy.maxHpLost", coop: true)!.Value);
        book.Clear("trophy.maxHpLost");
        Assert.Null(book.Best("trophy.maxHpLost", coop: true));
        Assert.Null(book.Personal("trophy.maxHpLost", 1, coop: true));
    }

    [Fact]
    public void Compositions_OrderAndGroup()
    {
        var runs = new[]
        {
            new Compositions.Run(new ulong[] { 1, 2 }, new[] { "IRONCLAD", "SILENT" }, 0, true),
            new Compositions.Run(new ulong[] { 2, 1 }, new[] { "IRONCLAD", "SILENT" }, 0, false),
            new Compositions.Run(new ulong[] { 1, 3 }, new[] { "DEFECT", "SILENT" }, 0, true),
            new Compositions.Run(new ulong[] { 1, 2 }, new[] { "DEFECT", "DEFECT" }, 0, true),
        };
        var all = Compositions.Of(runs);
        Assert.Equal("IRONCLAD+SILENT", all.Played(2).First().Key);
        Assert.Equal("IRONCLAD+SILENT", all.Played(2, order: Compositions.Order.WinRate, descending: false).First().Key);
        Assert.Equal("DEFECT+DEFECT", all.Played(2, order: Compositions.Order.WinRate).First().Key);
        var group = Compositions.OfGroup(runs, new ulong[] { 2, 1 });
        Assert.Equal(new Compositions.Tally(2, 1), group.Get("IRONCLAD+SILENT"));
        Assert.Equal(0, group.Get("DEFECT+SILENT").Played);
    }
}
