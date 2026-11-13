using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster;

/// Game modes (1.2.0, Zaille75: Blitz and Dread get their own stats). Blitz and Dread runs have their own records, fight
/// records and compositions (files "….blitz.cfg" / "….dread.cfg"); standard, daily and custom runs keep the existing files.
/// The mode is read by name: the game's GameMode values differ between branches (main v0.111.0: Custom = 3; beta v0.113.0:
/// Dread = 3, Blitz = 4, custom runs are Standard with IsCustomGame).
public static class GameModes
{
    public const string Standard = "standard";
    public const string Blitz = "blitz";
    public const string Dread = "dread";

    /// The modes with their own stats, in the order of the compositions window.
    public static readonly string[] WithStats = { Standard, Blitz, Dread };

    /// The mode of the run being played (or the last one): "standard", "daily", "custom", "blitz", "dread".
    public static string Current { get; set; } = Standard;

    public static string Of(IRunState run) => Kind(run.GameMode.ToString(), false);

    public static string Of(RunHistory history) => Kind(history.GameMode.ToString(), GameBranch.IsV113 && IsCustom(history));

    private static string Kind(string name, bool custom) => custom ? "custom" : name.ToLowerInvariant();

    /// Blitz and Dread have their own stats; every other mode shares the standard ones.
    public static bool Separate(string mode) => mode is Blitz or Dread;

    /// The stats a run of this mode goes to: "blitz", "dread" or "standard".
    public static string StatsOf(string mode) => Separate(mode) ? mode : Standard;

    /// File name part of a mode's stats: "" (standard), ".blitz", ".dread".
    public static string Suffix(string mode) => Separate(mode) ? "." + mode : "";

    /// "Blitz", "Dread", "Standard" (the game's own names, the same in French).
    public static string Name(string mode) => mode switch { Blitz => "Blitz", Dread => "Dread", _ => "Standard" };

    [BranchOnly, MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsCustom(RunHistory history) => history.IsCustomGame;
}
