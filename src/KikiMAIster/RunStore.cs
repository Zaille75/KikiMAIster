using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Runs;

namespace KikiMAIster;

/// Keeps the run totals across Save & Quit: one small file per run in "runs/" next to the DLL.
public static class RunStore
{
    private const int KeepFiles = 30;

    private static string? Dir
    {
        get
        {
            var dll = Path.GetDirectoryName(typeof(RunStore).Assembly.Location);
            return string.IsNullOrEmpty(dll) ? null : Path.Combine(dll, "runs");
        }
    }

    /// Same seed, ascension and players = same run (the game keeps the seed when a run is continued).
    public static string KeyOf(IRunState run) =>
        $"{run.Rng.StringSeed}_A{run.AscensionLevel}_{string.Join('-', run.Players.Select(p => p.NetId).OrderBy(id => id))}";

    private static string? PathOf(string key) =>
        Dir == null ? null : System.IO.Path.Combine(Dir, string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')) + ".txt");

    /// Called right after RunStarted. A saved file from a later floor means a new run on the same seed: ignored.
    public static void TryRestore(string key, int floor, StatsEngine engine) => Capture.Safe("RunStore.Restore", () =>
    {
        var path = PathOf(key);
        if (path == null || !File.Exists(path)) return;
        var snap = RunSnapshot.Read(File.ReadAllText(path));
        if (snap == null || snap.RunKey != key || snap.Floor > floor)
        {
            ModLog.Info($"run file ignored (saved floor {snap?.Floor}, current floor {floor})");
            return;
        }
        engine.RestoreRun(snap.Players, snap.Stats);
        ModLog.Info($"run totals restored from floor {snap.Floor} ({snap.Stats.Count} players)");
    });

    public static void Save(string key, int floor, StatsEngine engine) => Capture.Safe("RunStore.Save", () =>
    {
        var path = PathOf(key);
        if (path == null) return;
        Directory.CreateDirectory(Dir!);
        File.WriteAllText(path, RunSnapshot.Write(key, floor, engine));
        foreach (var old in new DirectoryInfo(Dir!).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepFiles))
            old.Delete();
    });
}
