using MegaCrit.Sts2.Core.Logging;

namespace KikiMAIster;

/// Writes to the game log and to KikiMAIster.log next to the mod DLL (easy to find and share).
public static class ModLog
{
    private static readonly object FileLock = new();
    private static readonly string? FilePath = InitFile();

    private static string? InitFile()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(ModLog).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return null;
            var path = Path.Combine(dir, "KikiMAIster.log");
            File.WriteAllText(path, $"=== KikiMAIster log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            return path;
        }
        catch
        {
            return null;
        }
    }

    public static void Info(string text) => Write("INFO", text);

    public static void Error(string text, Exception? e = null) => Write("ERROR", e == null ? text : $"{text}: {e}");

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
