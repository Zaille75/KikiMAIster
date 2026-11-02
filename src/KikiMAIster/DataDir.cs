using Godot;

namespace KikiMAIster;

/// Where the mod keeps its files (spec 03-technical, "Data folder", v1.0.1): "&lt;game user folder&gt;/KikiMAIster", outside
/// the mod's own folder, so a Steam Workshop update cannot wipe them. Falls back to the DLL folder.
/// Must not use ModLog (ModLog uses it): what happened is kept in <see cref="Note"/> and logged by ModLog.
public static class DataDir
{
    // Static initializers run in this order: Note must come before Folder (Init sets it), v1.0.2.
    /// One line for the log: where the data is and what was copied from an older version.
    public static string Note { get; private set; } = "";

    public static readonly string? DllFolder = GetDllFolder();

    public static readonly string? Folder = Init();

    public static string? File(string name) => Folder == null ? null : System.IO.Path.Combine(Folder, name);

    private static string? GetDllFolder()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(typeof(DataDir).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : dir;
        }
        catch
        {
            return null;
        }
    }

    private static string? Init()
    {
        try
        {
            var user = OS.GetUserDataDir();
            if (string.IsNullOrEmpty(user)) return Fallback("no user folder");
            var dir = System.IO.Path.Combine(user, "KikiMAIster");
            var fresh = !Directory.Exists(dir);
            Directory.CreateDirectory(dir);
            Note = $"Data folder: {dir}";
            if (fresh)
            {
                // A Workshop copy has no old files next to its DLL: the manual install in mods/ is looked at too.
                foreach (var old in OldFolders())
                {
                    if (SamePath(old, dir) || !Directory.Exists(old)) continue;
                    var copied = Migrate(old, dir);
                    if (copied > 0) Note += $" ({copied} files copied from {old})";
                }
            }
            return dir;
        }
        catch (Exception e)
        {
            return Fallback(e.Message);
        }
    }

    private static IEnumerable<string> OldFolders()
    {
        if (DllFolder != null) yield return DllFolder;
        string? mods = null;
        try
        {
            var game = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
            if (!string.IsNullOrEmpty(game)) mods = System.IO.Path.Combine(game, "mods", "KikiMAIster");
        }
        catch
        {
            // no game folder: only the DLL folder
        }
        if (mods != null && (DllFolder == null || !SamePath(mods, DllFolder))) yield return mods;
    }

    private static string? Fallback(string why)
    {
        Note = $"Data folder: {DllFolder} (user folder unusable: {why})";
        return DllFolder;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('/', '\\'), System.IO.Path.GetFullPath(b).TrimEnd('/', '\\'),
            StringComparison.OrdinalIgnoreCase);

    /// Copies the files of a version before 1.0.1 (never overwriting, the old files stay). Returns how many.
    private static int Migrate(string from, string to)
    {
        var count = 0;
        foreach (var file in Directory.GetFiles(from, "*.cfg")) count += Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)));
        foreach (var sub in new[] { "runs", "feedback" })
        {
            var src = System.IO.Path.Combine(from, sub);
            if (!Directory.Exists(src)) continue;
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                count += Copy(file, System.IO.Path.Combine(to, sub, System.IO.Path.GetRelativePath(src, file)));
        }
        return count;
    }

    private static int Copy(string src, string dst)
    {
        try
        {
            if (System.IO.File.Exists(dst)) return 0;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst)!);
            System.IO.File.Copy(src, dst);
            return 1;
        }
        catch
        {
            return 0;
        }
    }
}
