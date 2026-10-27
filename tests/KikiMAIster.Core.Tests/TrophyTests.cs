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
}
