using KikiMAIster.Core;

namespace KikiMAIster.Core.Tests;

public class AnonymizerTests
{
    private const ulong Kiki = 76561198000000001;
    private const ulong Max = 76561198000000002;

    [Fact]
    public void PlayersBecomeNumberedInTheOrderSeen()
    {
        var a = new Anonymizer();
        a.Register(Kiki, "Kiki");
        a.Register(Max, "Max Payne");
        var text = a.Scrub($"== Kiki (IRONCLAD, id {Kiki}): HP 10\nMax Payne/Silent hit; kiki blocked\npair.{Kiki}.{Max}=1;2");
        Assert.Equal("== Joueur 1 (IRONCLAD, id id-joueur-1): HP 10\nJoueur 2/Silent hit; Joueur 1 blocked\npair.id-joueur-1.id-joueur-2=1;2", text);
    }

    [Fact]
    public void NamesInsideLongerWordsAreKept()
    {
        var a = new Anonymizer();
        a.Register(Kiki, "Kiki");
        Assert.Equal("KikiMAIster log: Joueur 1.", a.Scrub("KikiMAIster log: Kiki."));
    }

    [Fact]
    public void ShortNamesAndSmallIdsAreLeftAlone()
    {
        var a = new Anonymizer();
        a.Register(1, "Bob");
        a.Register(Kiki, "Al");
        Assert.Equal("Al played 1 card, Bob too; id-joueur-1", a.Scrub($"Al played 1 card, Bob too; {Kiki}"));
    }

    [Fact]
    public void UnknownSteamIdsPathsAndMachineAreMasked()
    {
        var a = new Anonymizer();
        a.RegisterMachine("watson", "DESKTOP-42");
        var text = a.Scrub(@"lobby 76561198999999999 at C:\Users\watson\AppData\Roaming and /home/someone/x; watson on DESKTOP-42; seed 12345");
        Assert.Equal(@"lobby [steam-id] at C:\Users\[user]\AppData\Roaming and /home/[user]/x; [user] on [pc]; seed 12345", text);
    }

    [Fact]
    public void TheSamePlayerKeepsTheirNumber()
    {
        var a = new Anonymizer();
        a.Register(Max, null);
        a.Register(Kiki, "Kiki");
        a.Register(Max, "Max");
        Assert.Equal("Joueur 2 then Joueur 1", a.Scrub("Kiki then Max"));
    }
}
