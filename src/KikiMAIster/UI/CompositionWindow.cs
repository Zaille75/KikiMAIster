using System.Globalization;
using Godot;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Models;

namespace KikiMAIster.UI;

/// Group compositions (spec 02-ui v0.16.0): how often each composition was played and won, and a composition never
/// played / never won to try. Opened with F8 anywhere, the lobby included (the other windows only show during a run).
public static class CompositionWindow
{
    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static bool _rebuildPending;
    private static bool _dragging;
    private static int _players;
    /// The ascension level shown (null = every level).
    private static int? _ascension;
    /// Which suggestion is shown (changed by "Another").
    private static int _neverPlayedPick, _neverWonPick;
    private static readonly Random Rng = new();

    public static void Attach(CanvasLayer layer)
    {
        _panel = new PanelContainer { Visible = false, MouseDefaultCursorShape = Control.CursorShape.Move };
        _panel.AddThemeStyleboxOverride("panel", Overlay.WindowStyle(0.94f));
        _panel.GuiInput += OnDrag;
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 3);
        _panel.AddChild(_content);
        layer.AddChild(_panel);
        var timer = new Godot.Timer { WaitTime = 0.2, Autostart = true };
        timer.Timeout += () => Capture.Safe("CompositionWindow.Tick", () =>
        {
            if (!_rebuildPending || _panel is not { Visible: true }) return;
            _rebuildPending = false;
            Rebuild();
        });
        layer.AddChild(timer);
    }

    public static void Toggle()
    {
        if (_panel == null) return;
        if (_panel.Visible)
        {
            _panel.Visible = false;
            return;
        }
        var current = Overlay.InRun ? Capture.Engine.Players.Count : 0;
        _players = current > 0 ? current : _players > 0 ? _players : 2;
        _ascension = current > 0 ? Patches.CombatFlowPatches.Ascension : CompositionStore.Data.MostPlayedAscension();
        Pick();
        Rebuild();
        _panel.Visible = true;
        var screen = _panel.GetViewportRect().Size;
        _panel.Position = new Vector2(Math.Max(0, screen.X / 2 - 220), 120);
    }

    /// Rebuilds the open window on the next tick (language changed).
    public static void Refresh() => _rebuildPending = true;

    private static void OnDrag(InputEvent e)
    {
        if (_panel == null) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb: _dragging = mb.Pressed; break;
            case InputEventMouseMotion mm when _dragging: _panel.Position += mm.Relative; break;
        }
    }

    /// The game's characters ("IRONCLAD"…).
    private static IReadOnlyList<string> Characters()
    {
        try
        {
            return ModelDb.AllCharacters.Select(c => c.Id.Entry).ToList();
        }
        catch (Exception e)
        {
            ModLog.Error("characters", e);
            return new[] { "IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT" };
        }
    }

    private static void Pick()
    {
        _neverPlayedPick = Rng.Next(1000);
        _neverWonPick = Rng.Next(1000);
    }

    /// "2× La Silencieuse + Le Soldat de Fer".
    internal static string Describe(string key) =>
        string.Join(" + ", Compositions.Members(key).GroupBy(c => c).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Count() > 1 ? $"{g.Count()}× " : "") + GameNames.Of($"Character:{g.Key}")));

    /// "3 games, 1 win (33%)" or "never played".
    internal static string Stats(Compositions.Tally t) =>
        t.Played == 0 ? Loc.T("compo.never")
            : string.Format(Loc.T("compo.stats"), t.Played, t.Won, t.WinRate.ToString("P0", CultureInfo.InvariantCulture));

    /// The current run's composition and its record at this ascension level before this run ("" in solo or outside a run).
    internal static string CurrentLine()
    {
        var players = Capture.Engine.Players.Values.ToList();
        if (players.Count < 2) return "";
        var key = Compositions.KeyOf(players.Select(p => p.Character));
        var level = Patches.CombatFlowPatches.Ascension;
        return $"{Loc.T("compo.group")} {Describe(key)} · {string.Format(Loc.T("compo.at"), level)}{Stats(CompositionStore.Data.Get(key, level))}";
    }

    private static void Rebuild()
    {
        if (_panel == null || _content == null) return;
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
        _panel.CallDeferred(Control.MethodName.ResetSize);
        var data = CompositionStore.Data;
        var characters = Characters();

        var header = new HBoxContainer();
        var title = Overlay.MakeLabel(Loc.T("compo.title"), Overlay.Accent);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("CompositionWindow.Close", () => _panel!.Visible = false);
        header.AddChild(close);
        _content.AddChild(header);

        var count = new HBoxContainer();
        count.AddChild(Overlay.MakeLabel(Loc.T("compo.players"), Overlay.Text));
        foreach (var n in new[] { 1, 2, 3, 4 })
        {
            var b = Overlay.MakeButton(n.ToString(CultureInfo.InvariantCulture));
            b.AddThemeColorOverride("font_color", n == _players ? Overlay.Accent : Overlay.Muted);
            b.Pressed += () => Capture.Safe("CompositionWindow.Players", () =>
            {
                _players = n;
                Pick();
                _rebuildPending = true;
            });
            count.AddChild(b);
        }
        _content.AddChild(count);

        // Ascension level: every level, or one of the levels played (and the current run's).
        var levels = new HBoxContainer();
        levels.AddChild(Overlay.MakeLabel(Loc.T("compo.ascension"), Overlay.Text));
        var played = data.Ascensions().ToList();
        if (Overlay.InRun && !played.Contains(Patches.CombatFlowPatches.Ascension)) played.Add(Patches.CombatFlowPatches.Ascension);
        if (_ascension is { } chosen && !played.Contains(chosen)) played.Add(chosen);
        foreach (var level in new int?[] { null }.Concat(played.OrderByDescending(a => a).Select(a => (int?)a)))
        {
            var b = Overlay.MakeButton(level == null ? Loc.T("compo.allLevels") : $"A{level.Value.ToString(CultureInfo.InvariantCulture)}");
            b.AddThemeColorOverride("font_color", level == _ascension ? Overlay.Accent : Overlay.Muted);
            b.Pressed += () => Capture.Safe("CompositionWindow.Ascension", () =>
            {
                _ascension = level;
                Pick();
                _rebuildPending = true;
            });
            levels.AddChild(b);
        }
        _content.AddChild(levels);

        if (Overlay.InRun && CurrentLine() is { Length: > 0 } current) _content.AddChild(Overlay.MakeLabel(current, Overlay.Text));

        // Played compositions, most played first.
        var compositions = data.Played(_players, _ascension).ToList();
        if (compositions.Count == 0) _content.AddChild(Overlay.MakeLabel(Loc.T("compo.none"), Overlay.Muted));
        else
        {
            var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Pass };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 0);
            foreach (var (key, tally) in compositions.Take(12))
            {
                grid.AddChild(Overlay.MakeLabel(Describe(key), Overlay.Text));
                grid.AddChild(Overlay.MakeLabel(Stats(tally), tally.Won > 0 ? Overlay.Text : Overlay.Muted));
            }
            _content.AddChild(grid);
        }

        // Suggestions: a composition never played, one never won.
        AddSuggestion("compo.neverPlayed", data.NeverPlayed(characters, _players, _ascension).ToList(), _neverPlayedPick, () => _neverPlayedPick++);
        AddSuggestion("compo.neverWon", data.NeverWon(characters, _players, _ascension).ToList(), _neverWonPick, () => _neverWonPick++);
        _content.AddChild(Overlay.MakeLabel(Loc.T("compo.note"), Overlay.Muted));
    }

    private static void AddSuggestion(string labelKey, List<string> options, int pick, Action next)
    {
        var row = new HBoxContainer();
        row.AddChild(Overlay.MakeLabel(string.Format(Loc.T(labelKey), options.Count), Overlay.Accent));
        row.AddChild(Overlay.MakeLabel(options.Count == 0 ? "-" : Describe(options[pick % options.Count]), Overlay.Text));
        if (options.Count > 1)
        {
            var another = Overlay.MakeButton(Loc.T("compo.another"));
            another.Pressed += () => Capture.Safe("CompositionWindow.Another", () =>
            {
                next();
                _rebuildPending = true;
            });
            row.AddChild(another);
        }
        _content!.AddChild(row);
    }
}

/// Composition tallies, saved next to the DLL (not a .json file: the game reads every .json in mods/ as a manifest).
public static class CompositionStore
{
    private static Compositions? _data;

    private static string? FilePath
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(CompositionStore).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "KikiMAIster.compositions.cfg");
        }
    }

    public static Compositions Data
    {
        get
        {
            if (_data != null) return _data;
            _data = new Compositions();
            Capture.Safe("CompositionStore.Load", () =>
            {
                if (FilePath != null && File.Exists(FilePath)) _data = Compositions.Read(File.ReadAllText(FilePath));
            });
            return _data;
        }
    }

    /// A finished run (victory or defeat) counts one game for its composition.
    public static void Add(IEnumerable<string> characters, int ascension, bool victory) => Capture.Safe("CompositionStore.Save", () =>
    {
        var list = characters.ToList();
        Data.Add(list, ascension, victory);
        if (FilePath != null) File.WriteAllText(FilePath, Data.Write());
        ModLog.Info($"composition A{ascension} {Compositions.KeyOf(list)}: {(victory ? "victory" : "defeat")} recorded");
    });
}
