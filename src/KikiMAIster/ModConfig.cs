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
    /// Open the combat summary by itself when a combat ends.
    public bool AutoSummary { get; set; } = true;
    /// Summary window position; negative = default (top center).
    public float SummaryX { get; set; } = -1;
    public float SummaryY { get; set; } = -1;
    /// Settings window position; negative = next to the panel.
    public float SettingsX { get; set; } = -1;
    public float SettingsY { get; set; } = -1;
    /// Sections and columns hidden in the settings: "section:<key>" and "col:<section>:<column>".
    public HashSet<string> Hidden { get; set; } = new();
    /// Summary chart: per-round values instead of cumulative totals.
    public bool ChartPerRound { get; set; }
    /// Hide a column when every player shows 0 in it (v0.17.0, on by default).
    public bool HideZeroColumns { get; set; } = true;
    /// Show the group's composition and record line in the panel (v0.17.0, off by default).
    public bool ShowGroupLine { get; set; }
    /// Compositions window: order by "games" or "winrate", descending or not, and stats of this group of players only.
    public string CompoOrder { get; set; } = "games";
    public bool CompoDescending { get; set; } = true;
    public bool CompoGroupOnly { get; set; }
    /// Lobby (v0.17.10): the KikiMAIster panel is hidden by default there (F7 shows it); the Compositions window opens
    /// by itself, and can be folded to its title. All remembered.
    public bool LobbyPanelVisible { get; set; }
    public bool CompoLobbyOpen { get; set; } = true;
    public bool CompoFolded { get; set; }
    /// 2 players: grid (my character × my partner's) instead of the list.
    public bool CompoGrid { get; set; }
    /// Sections shown in their own window (v0.17.0), and each window's position ("section" → x, y).
    public HashSet<string> Detached { get; set; } = new();
    public Dictionary<string, (float X, float Y)> DetachedPositions { get; set; } = new();
    /// Trophies window (v0.17.45): the fun trophies' section shown, the chifumi records' section open.
    public bool FunTrophiesShown { get; set; } = true;
    public bool ChifumiRecordsOpen { get; set; } = true;
    /// The "Maître des malus" records were cleared once (v0.17.1: they counted À l'attaque !, which is no debuff).
    public bool DebuffRecordsCleared { get; set; }
    /// The "Champion des erreurs" records were cleared once (v0.18.9, Zaille75: the misplay rules changed).
    public bool MissedRecordsCleared { get; set; }
    /// Hotkeys (v0.18.9, Zaille75): the panel, the group compositions and the report window (Godot key names).
    public string KeyPanel { get; set; } = "F7";
    public string KeyCompositions { get; set; } = "F8";
    public string KeyReport { get; set; } = "F9";
    /// Dates shown by the mod (v0.18.5, Zaille75): "dmy" (DD/MM/YYYY, default), "ymd" (YYYY/MM/DD) or "ydm" (YYYY/DD/MM).
    public string DateFormat { get; set; } = "dmy";

    /// A date saved as "yyyy-MM-dd", shown in the chosen format (left as is when it is not such a date).
    public string ShowDate(string iso)
    {
        if (iso.Length != 10 || iso[4] != '-' || iso[7] != '-') return iso;
        var (y, m, d) = (iso[..4], iso[5..7], iso[8..]);
        return DateFormat switch { "ymd" => $"{y}/{m}/{d}", "ydm" => $"{y}/{d}/{m}", _ => $"{d}/{m}/{y}" };
    }

    /// Hidden columns that moved (v0.18.4: potions and energy left Cartes; v0.18.5: HP columns left Dégâts for PV, and
    /// Énergie en trop / pour rien became one Énergie inutile column).
    private static string MigrateHidden(string h) => h switch
    {
        "col:other:col.potions" => "col:energy:col.potions",
        "col:other:col.energySurplus" or "col:other:col.energyUseless" or "col:energy:col.energySurplus" or "col:energy:col.energyUseless"
            => "col:energy:col.energyUnused",
        _ when h.StartsWith("col:damage:") && h[11..] is "col.avoided" or "col.hpLost" or "col.fromSelf" or "col.fromAllies" or "col.hpOutside"
            or "col.healOutside" or "col.maxHp" => "col:hp:" + h[11..],
        _ => h
    };

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
                    case "scope": c.Scope = v == "Turn" ? "PreviousTurn" : v; break;
                    case "opacity": c.Opacity = Float(v, c.Opacity); break;
                    case "scale": c.Scale = Float(v, c.Scale); break;
                    case "language": c.Language = v; break;
                    case "autoSummary": c.AutoSummary = v == "true"; break;
                    case "summaryX": c.SummaryX = float.Parse(v, CultureInfo.InvariantCulture); break;
                    case "summaryY": c.SummaryY = float.Parse(v, CultureInfo.InvariantCulture); break;
                    case "settingsX": c.SettingsX = Float(v, -1); break;
                    case "settingsY": c.SettingsY = Float(v, -1); break;
                    case "hidden": c.Hidden = v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(MigrateHidden).ToHashSet(); break;
                    case "dateFormat": c.DateFormat = v; break;
                    case "chartPerRound": c.ChartPerRound = v == "true"; break;
                    case "hideZeroColumns": c.HideZeroColumns = v == "true"; break;
                    case "showGroupLine": c.ShowGroupLine = v == "true"; break;
                    case "compoOrder": c.CompoOrder = v; break;
                    case "compoDescending": c.CompoDescending = v == "true"; break;
                    case "compoGroupOnly": c.CompoGroupOnly = v == "true"; break;
                    case "compoGrid": c.CompoGrid = v == "true"; break;
                    case "lobbyPanelVisible": c.LobbyPanelVisible = v == "true"; break;
                    case "compoLobbyOpen": c.CompoLobbyOpen = v == "true"; break;
                    case "compoFolded": c.CompoFolded = v == "true"; break;
                    case "debuffRecordsCleared": c.DebuffRecordsCleared = v == "true"; break;
                    case "missedRecordsCleared": c.MissedRecordsCleared = v == "true"; break;
                    case "keyPanel": c.KeyPanel = v; break;
                    case "keyCompositions": c.KeyCompositions = v; break;
                    case "keyReport": c.KeyReport = v; break;
                    case "funTrophiesShown": c.FunTrophiesShown = v == "true"; break;
                    case "chifumiRecordsOpen": c.ChifumiRecordsOpen = v == "true"; break;
                    case "detached": c.Detached = v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); break;
                    default:
                        // "pos.<section>=x;y": a detached section's window.
                        if (k.StartsWith("pos.") && v.Split(';') is { Length: 2 } xy)
                            c.DetachedPositions[k[4..]] = (Float(xy[0], 20), Float(xy[1], 160));
                        break;
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
                $"language={Language}", $"autoSummary={(AutoSummary ? "true" : "false")}",
                $"summaryX={SummaryX.ToString(CultureInfo.InvariantCulture)}", $"summaryY={SummaryY.ToString(CultureInfo.InvariantCulture)}",
                $"settingsX={SettingsX.ToString(ci)}", $"settingsY={SettingsY.ToString(ci)}",
                $"hidden={string.Join(',', Hidden)}", $"chartPerRound={(ChartPerRound ? "true" : "false")}",
                $"hideZeroColumns={(HideZeroColumns ? "true" : "false")}", $"showGroupLine={(ShowGroupLine ? "true" : "false")}",
                $"compoOrder={CompoOrder}", $"compoDescending={(CompoDescending ? "true" : "false")}",
                $"compoGroupOnly={(CompoGroupOnly ? "true" : "false")}", $"compoGrid={(CompoGrid ? "true" : "false")}",
                $"lobbyPanelVisible={(LobbyPanelVisible ? "true" : "false")}", $"compoLobbyOpen={(CompoLobbyOpen ? "true" : "false")}",
                $"compoFolded={(CompoFolded ? "true" : "false")}", $"detached={string.Join(',', Detached)}",
                $"debuffRecordsCleared={(DebuffRecordsCleared ? "true" : "false")}",
                $"missedRecordsCleared={(MissedRecordsCleared ? "true" : "false")}",
                $"keyPanel={KeyPanel}", $"keyCompositions={KeyCompositions}", $"keyReport={KeyReport}",
                $"funTrophiesShown={(FunTrophiesShown ? "true" : "false")}", $"chifumiRecordsOpen={(ChifumiRecordsOpen ? "true" : "false")}",
                $"dateFormat={DateFormat}"
            }.Concat(DetachedPositions.Select(kv => $"pos.{kv.Key}={kv.Value.X.ToString(ci)};{kv.Value.Y.ToString(ci)}")));
        }
        catch (Exception e)
        {
            ModLog.Error("could not save settings", e);
        }
    }
}
