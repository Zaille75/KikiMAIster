using System.Globalization;

namespace KikiMAIster;

/// User settings, saved next to the mod DLL as simple "key=value" lines.
/// Not a .json file: the game treats every .json in mods/ as a manifest. No System.Text.Json reflection
/// either, which the game runtime may have disabled.
public sealed class ModConfig
{
    public float X { get; set; } = 20;
    public float Y { get; set; } = 160;
    public bool Visible { get; set; } = true;
    public string Scope { get; set; } = "Combat";
    public float Opacity { get; set; } = 0.78f;
    public float Scale { get; set; } = 1f;
    /// "auto" (game language), "en" or "fr".
    public string Language { get; set; } = "auto";
    /// Not saved: every section starts folded when the game starts.
    public List<string> OpenSections { get; set; } = new();

    public static ModConfig Current { get; private set; } = Load();

    private static string? FilePath
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(ModConfig).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "KikiMAIster.settings.cfg");
        }
    }

    private static ModConfig Load()
    {
        var c = new ModConfig();
        try
        {
            if (FilePath == null || !File.Exists(FilePath)) return c;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                var (k, v) = (line[..i].Trim(), line[(i + 1)..].Trim());
                switch (k)
                {
                    case "x": c.X = Float(v, c.X); break;
                    case "y": c.Y = Float(v, c.Y); break;
                    case "visible": c.Visible = v == "true"; break;
                    case "scope": c.Scope = v; break;
                    case "opacity": c.Opacity = Float(v, c.Opacity); break;
                    case "scale": c.Scale = Float(v, c.Scale); break;
                    case "language": c.Language = v; break;
                }
            }
        }
        catch (Exception e)
        {
            ModLog.Error("could not read settings, using defaults", e);
        }
        return c;
    }

    private static float Float(string v, float fallback) =>
        float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : fallback;

    public void Save()
    {
        try
        {
            if (FilePath == null) return;
            var ci = CultureInfo.InvariantCulture;
            File.WriteAllLines(FilePath, new[]
            {
                $"x={X.ToString(ci)}", $"y={Y.ToString(ci)}", $"visible={(Visible ? "true" : "false")}",
                $"scope={Scope}", $"opacity={Opacity.ToString(ci)}", $"scale={Scale.ToString(ci)}",
                $"language={Language}"
            });
        }
        catch (Exception e)
        {
            ModLog.Error("could not save settings", e);
        }
    }
}
