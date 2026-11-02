using MegaCrit.Sts2.Core.Logging;

namespace KikiMAIster;

/// Writes to the game log and to KikiMAIster.log in the mod's data folder (DataDir, v1.0.1).
public static class ModLog
{
    private static readonly object FileLock = new();
    private static readonly string? FilePath = InitFile();

    private static string? InitFile()
    {
        try
        {
            var dir = DataDir.Folder;
            if (string.IsNullOrEmpty(dir)) return null;
            var path = Path.Combine(dir, "KikiMAIster.log");
            // The previous session's log is kept (a crash or a restart no longer loses it, v0.17.1).
            if (File.Exists(path)) File.Copy(path, Path.Combine(dir, "KikiMAIster.previous.log"), overwrite: true);
            File.WriteAllText(path, $"=== KikiMAIster log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n{DataDir.Note}\n");
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static void Info(string text) => Write("INFO", text);

    public static void Error(string text, Exception? e = null)
    {
        var full = e == null ? text : $"{text}: {e}";
        Note(text, full);
        Write("ERROR", full);
    }

    /// The internal errors of this session, by place (spec 03-technical, "Reports"): attached to F9 reports.
    private static readonly Dictionary<string, (int Count, string First)> NotedErrors = new();
    private const int MaxNotedPlaces = 30;
    private static int _unreported;

    /// Errors noted since the last report: the panel shows a hint while it is above 0.
    public static int UnreportedErrors => Volatile.Read(ref _unreported);

    public static int NotedErrorCount
    {
        get { lock (NotedErrors) return NotedErrors.Count; }
    }

    public static void ErrorsReported() => Volatile.Write(ref _unreported, 0);

    private static void Note(string where, string full)
    {
        try
        {
            lock (NotedErrors)
            {
                if (NotedErrors.TryGetValue(where, out var seen)) NotedErrors[where] = (seen.Count + 1, seen.First);
                else if (NotedErrors.Count < MaxNotedPlaces) NotedErrors[where] = (1, full);
                else return;
            }
            Interlocked.Increment(ref _unreported);
        }
        catch
        {
            // Noting must never break the game.
        }
    }

    /// errors.txt: each place, how many times, and the first error's full text.
    public static string ErrorsText()
    {
        lock (NotedErrors)
        {
            if (NotedErrors.Count == 0) return "No internal error noted this session.\n";
            return string.Join("\n", NotedErrors.Select(kv => $"== {kv.Key} (x{kv.Value.Count})\n{kv.Value.First}\n"));
        }
    }

    private static void Write(string level, string text)
    {
        var line = $"[{ModEntry.Id}] {text}";
        try
        {
            if (level == "ERROR") Log.Error(line); else Log.Info(line);
            if (FilePath != null)
                lock (FileLock) File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss} {level} {text}\n");
        }
        catch
        {
            // Logging must never break the game.
        }
    }
}
