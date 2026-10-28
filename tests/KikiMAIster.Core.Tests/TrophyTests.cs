using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

public class TrophyTests
{
    private static Dictionary<ulong, PlayerStats> Run(double damageA, double damageB, double hpA = 10, double hpB = 20) => new()
    {
        [1] = new PlayerStats { DamageDealt = damageA, HpLostFromEnemies = hpA },
        [2] = new PlayerStats { DamageDealt = damageB, HpLostFromEnemies = hpB },
    };

    private static readonly Dictionary<ulong, string> Names = new() { [1] = "Kiki", [2] = "Max" };

    [Fact]
    public void Winner_HighestOrLowest()
    {
        var run = Run(100, 150);
        Assert.Equal(2UL, Trophies.Winner(Trophies.All.First(c => c.Key == "trophy.damage"), run)!.Value.Id);
        Assert.Equal(1UL, Trophies.Winner(Trophies.All.First(c => c.Key == "trophy.untouched"), run)!.Value.Id);
        Assert.Null(Trophies.Winner(Trophies.All.First(c => c.Key == "trophy.deaths"), run));
    }

    [Fact]
    public void Records_AllTimeAndPersonal_SurviveRoundTrip()
    {
        var book = new RecordBook();
        var first = book.Add(Run(100, 150), Names, victory: false, "2026-10-27");
        Assert.Contains("trophy.damage", first.NewRecords);
        Assert.DoesNotContain("trophy.untouched", first.NewRecords); // defeat: lower-is-better not counted

        book = RecordBook.Read(book.Write());
        var second = book.Add(Run(120, 90), Names, victory: true, "2026-10-28");
        Assert.DoesNotContain("trophy.damage", second.NewRecords);
        Assert.Contains(("trophy.damage", 1UL), second.NewPersonal);
        Assert.Equal(150, book.Best("trophy.damage", coop: true)!.Value);
        Assert.Equal("Max", book.Best("trophy.damage", coop: true)!.Name);
        Assert.Equal(120, book.Personal("trophy.damage", 1, coop: true)!.Value);
        Assert.Equal(10, book.Best("trophy.untouched", coop: true)!.Value);
    }

    [Fact]
    public void Records_PerCharacter()
    {
        var book = new RecordBook();
        var chars = new Dictionary<ulong, string> { [1] = "IRONCLAD", [2] = "SILENT" };
        var first = book.Add(Run(100, 150), Names, victory: true, "2026-10-27", chars);
        Assert.Contains(("trophy.damage", "IRONCLAD"), first.NewCharacter);
        Assert.Equal(150, book.OfCharacter("trophy.damage", "SILENT", coop: true)!.Value);

        book = RecordBook.Read(book.Write());
        // Another player with the Ironclad beats Kiki's Ironclad record, not the all-time one.
        var second = book.Add(new Dictionary<ulong, PlayerStats> { [3] = new() { DamageDealt = 120 }, [4] = new() { DamageDealt = 10 } },
            new Dictionary<ulong, string> { [3] = "Zed", [4] = "Lou" }, victory: true, "2026-10-28",
            new Dictionary<ulong, string> { [3] = "IRONCLAD", [4] = "DEFECT" });
        Assert.Contains(("trophy.damage", "IRONCLAD"), second.NewCharacter);
        Assert.DoesNotContain("trophy.damage", second.NewRecords);
        Assert.Equal("Zed", book.OfCharacter("trophy.damage", "IRONCLAD", coop: true)!.Name);
        Assert.Equal(new[] { "DEFECT", "IRONCLAD", "SILENT" }, book.Characters("trophy.damage", coop: true).Select(x => x.Character));
    }

    [Fact]
    public void Records_SoloAndCoopAreSeparate()
    {
        var book = new RecordBook();
        book.Add(new Dictionary<ulong, PlayerStats> { [1] = new() { DamageDealt = 300 } }, Names, victory: true, "2026-10-27");
        var coop = book.Add(Run(100, 150), Names, victory: true, "2026-10-28");
        Assert.Contains("trophy.damage", coop.NewRecords);
        book = RecordBook.Read(book.Write());
        Assert.Equal(300, book.Best("trophy.damage")!.Value);
        Assert.Equal(150, book.Best("trophy.damage", coop: true)!.Value);
        Assert.Equal(100, book.Personal("trophy.damage", 1, coop: true)!.Value);
    }

    [Fact]
    public void Compositions_CountGamesAndSuggest()
    {
        var c = new Compositions();
        c.Add(new[] { "SILENT", "IRONCLAD", "SILENT" }, 10, victory: false);
        c.Add(new[] { "SILENT", "SILENT", "IRONCLAD" }, 10, victory: true);
        c.Add(new[] { "DEFECT", "IRONCLAD" }, 10, victory: false);
        c.Add(new[] { "DEFECT", "IRONCLAD" }, 3, victory: true);
        c = Compositions.Read(c.Write());
        var key = Compositions.KeyOf(new[] { "IRONCLAD", "SILENT", "SILENT" });
        Assert.Equal(new Compositions.Tally(2, 1), c.Get(key));
        Assert.Equal(0.5, c.Get(key).WinRate);

        var chars = new[] { "IRONCLAD", "SILENT", "DEFECT" };
        Assert.Equal(6, Compositions.AllPossible(chars, 2).Count()); // 3 pairs + 3 doubles
        Assert.Equal(10, Compositions.AllPossible(chars, 3).Count());
        Assert.Equal(5, c.NeverPlayed(chars, 2).Count());
        Assert.Empty(c.NeverWon(chars, 2)); // all levels: won once at A3
        Assert.Equal(new[] { "DEFECT+IRONCLAD" }, c.NeverWon(chars, 2, 10));
        Assert.Equal(new Compositions.Tally(1, 1), c.Get("DEFECT+IRONCLAD", 3));
        Assert.Equal(new Compositions.Tally(2, 1), c.Get("DEFECT+IRONCLAD"));
        Assert.Equal(new[] { 10, 3 }, c.Ascensions());
        Assert.Equal(10, c.MostPlayedAscension());
        Assert.Single(c.Played(3, 10));
        Assert.Empty(c.Played(3, 3));
    }

    [Fact]
    public void Records_PerAscensionLevel_AndEveryLevel()
    {
        var book = new RecordBook();
        book.Add(Run(100, 150), Names, victory: true, "2026-10-28", ascension: 10);
        var low = book.Add(Run(400, 10), Names, victory: true, "2026-10-28", ascension: 1);
        Assert.Contains("trophy.damage", low.NewRecords);
        Assert.Equal(150, book.Best("trophy.damage", coop: true, ascension: 10)!.Value);
        Assert.Equal(400, book.Best("trophy.damage", coop: true, ascension: 1)!.Value);
        Assert.Equal(400, book.Best("trophy.damage", coop: true)!.Value);
        book = RecordBook.Read(book.Write());
        Assert.Equal(150, book.Best("trophy.damage", coop: true, ascension: 10)!.Value);
        book.Clear("trophy.damage");
        Assert.Null(book.Best("trophy.damage", coop: true, ascension: 10));
        Assert.Null(book.Best("trophy.damage", coop: true));
    }

    [Fact]
    public void MasterBuilder_LeastUselessBlock_VictoryOnly()
    {
        var c = Trophies.All.First(x => x.Key == "trophy.efficientBlock");
        var run = new Dictionary<ulong, PlayerStats>
        {
            [1] = new() { BlockGainedSelf = 100, UselessBlock = 20 },
            [2] = new() { BlockGainedSelf = 50, UselessBlock = 5 },
        };
        Assert.Equal((2UL, 5.0), Trophies.Winner(c, run));
        var book = new RecordBook();
        book.Add(run, Names, victory: false, "2026-10-28");
        Assert.Null(book.Best("trophy.efficientBlock", coop: true));
        book.Add(run, Names, victory: true, "2026-10-28");
        Assert.Equal(5, book.Best("trophy.efficientBlock", coop: true)!.Value);
    }
}
