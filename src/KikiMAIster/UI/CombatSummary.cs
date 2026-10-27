using System.Globalization;
using Godot;
using KikiMAIster.Core;

namespace KikiMAIster.UI;

/// Combat summary (spec 02-ui §2): a cumulative chart per round, one line per player, for a chosen stat.
/// A selector picks the combat: the one in progress (updated live) or any finished combat of the run.
/// Opens by itself when a combat ends; the overlay's "Summary" button opens it at any time.
public static class CombatSummary
{
    private sealed record Metric(string Key, Func<RoundTotals, double> Value, bool CoopOnly = false);

    private static readonly Metric[] Metrics =
    {
        new("summary.damage", t => t.Damage),
        new("summary.hpLost", t => t.HpLost),
        new("summary.debuffs", t => t.DebuffStacks),
        new("summary.saved", t => t.Saved, CoopOnly: true),
    };

    private const float ChartWidth = 420, ChartHeight = 200, AxisLeft = 40, AxisBottom = 20;

    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static int _shownCombat;
    private static int _selected;
    private static string _liveSignature = "";
    private static bool _rebuildPending;
    /// Opened by itself at the end of a combat: closed again when the map opens.
    private static bool _autoShown;
    private static string _metric = "summary.damage";

    private static ModConfig Cfg => ModConfig.Current;

    public static void Attach(CanvasLayer layer)
    {
        _panel = new PanelContainer { Visible = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.08f, Math.Max(Cfg.Opacity, 0.9f)),
            BorderColor = new Color(Overlay.Accent, 0.6f),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8
        });
        _panel.MouseDefaultCursorShape = Control.CursorShape.Move;
        _panel.GuiInput += OnDrag;
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 6);
        _panel.AddChild(_content);
        layer.AddChild(_panel);
    }

    /// True when there is something to show (a combat in progress or finished in this run).
    public static bool HasData => Capture.Engine.CurrentCombat != null || Capture.Engine.LastCombat != null;

    /// Called by the overlay timer: opens the summary once for each newly finished combat, and keeps the
    /// chart of the combat in progress up to date.
    public static void ShowIfNewCombatEnded()
    {
        var last = Capture.Engine.LastCombat;
        if (last != null && last.Number != _shownCombat)
        {
            _shownCombat = last.Number;
            if (!Cfg.AutoSummary) return;
            _selected = last.Number;
            Show();
            _autoShown = true;
            return;
        }
        KeepClear();
        if (_panel is not { Visible: true } || Selected() is not { } selected) return;
        if (_rebuildPending || (selected.InProgress && Signature(selected) != _liveSignature))
        {
            _rebuildPending = false;
            Rebuild(selected);
        }
    }

    /// Rebuilds the open window on the next tick (language changed).
    public static void Refresh() => _rebuildPending = true;

    /// The window's rectangle on screen when it is open (the trophies window keeps out of it).
    internal static Rect2? PanelRect => _panel is { Visible: true } p && GodotObject.IsInstanceValid(p) ? new Rect2(p.Position, p.Size) : null;

    public static void Hide()
    {
        if (_panel != null && _panel.Visible) _panel.Visible = false;
    }

    public static void Toggle()
    {
        if (_panel == null) return;
        _autoShown = false;
        if (_panel.Visible)
        {
            _panel.Visible = false;
            return;
        }
        // Opens on the combat in progress, else on the last finished one.
        _selected = (Capture.Engine.CurrentCombat ?? Capture.Engine.LastCombat)?.Number ?? 0;
        Show();
    }

    /// Back on the map (rewards done) or a new combat: closes a summary that opened by itself.
    public static void HideAutoShown()
    {
        if (!_autoShown || _panel == null) return;
        _autoShown = false;
        _panel.Visible = false;
    }

    /// The selected combat: in progress (live) or finished; falls back to the newest one.
    private static CombatRecord? Selected()
    {
        var engine = Capture.Engine;
        if (engine.CurrentCombat is { } live && live.Number == _selected) return live;
        return engine.Combats.FirstOrDefault(c => c.Number == _selected) ?? engine.CurrentCombat ?? engine.LastCombat;
    }

    private static string Signature(CombatRecord c) =>
        $"{c.Number}:{c.LastRound}:" + string.Join(',', c.Rounds[^1].Players.Select(kv =>
            $"{kv.Value.Damage:F0}/{kv.Value.Prevented:F0}/{kv.Value.HpLost:F0}/{kv.Value.DebuffStacks:F0}/{kv.Value.Saved:F0}"));

    private static void Show()
    {
        if (_panel == null || _content == null || Selected() is not { } combat) return;
        Rebuild(combat);
        _panel.Visible = true;
        _panel.CallDeferred(Control.MethodName.ResetSize);
        // Where the player left it, else top center of the screen (above the rewards); always kept on screen.
        var screen = _panel.GetViewportRect().Size;
        var x = Cfg.SummaryX >= 0 ? Cfg.SummaryX : (screen.X - ChartWidth - 40) / 2;
        var y = Cfg.SummaryY >= 0 ? Cfg.SummaryY : 70;
        _panel.Position = new Vector2(Math.Clamp(x, 0, Math.Max(0, screen.X - ChartWidth - 40)), Math.Clamp(y, 0, Math.Max(0, screen.Y - 120)));
        _panel.CallDeferred(Control.MethodName.ResetSize);
    }

    private static bool _dragging;

    /// Drag the window by any empty area (buttons and the combat picker keep their clicks); position saved.
    private static void OnDrag(InputEvent e)
    {
        if (_panel == null) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                if (!mb.Pressed)
                {
                    Cfg.SummaryX = _panel.Position.X;
                    Cfg.SummaryY = _panel.Position.Y;
                    Cfg.Save();
                }
                break;
            case InputEventMouseMotion mm when _dragging:
                _panel.Position += mm.Relative;
                break;
        }
    }

    private static void Rebuild(CombatRecord combat)
    {
        foreach (var child in _content!.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        _selected = combat.Number;
        _liveSignature = combat.InProgress ? Signature(combat) : "";

        // Combat selector (in progress first, then finished combats, newest first) + close.
        var header = new HBoxContainer();
        var picker = new OptionButton { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        picker.AddThemeFontSizeOverride("font_size", Overlay.FontSize);
        var choices = new List<CombatRecord>();
        if (Capture.Engine.CurrentCombat is { } live) choices.Add(live);
        choices.AddRange(Capture.Engine.Combats.Reverse());
        foreach (var c in choices)
        {
            picker.AddItem(Label(c), c.Number);
            if (c.Number == combat.Number && c.InProgress == combat.InProgress) picker.Select(picker.ItemCount - 1);
        }
        picker.ItemSelected += index => Capture.Safe("CombatSummary.Pick", () =>
        {
            // Rebuilt on the next timer tick: the picker cannot remove itself while its popup is closing.
            _selected = picker.GetItemId((int)index);
            _rebuildPending = true;
        });
        header.AddChild(picker);
        var trophies = Overlay.MakeButton(Loc.T("run.button"));
        trophies.TooltipText = Loc.T("run.buttonTip");
        trophies.Pressed += () => Capture.Safe("CombatSummary.Trophies", RunSummary.Toggle);
        header.AddChild(trophies);
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("CombatSummary.Close", () => _panel!.Visible = false);
        header.AddChild(close);
        _content.AddChild(header);

        // Stat selector, then cumulative / per round.
        var coop = Players(combat).Count > 1;
        var tabs = new HBoxContainer();
        foreach (var m in Metrics.Where(m => coop || !m.CoopOnly))
        {
            var b = Overlay.MakeButton(Loc.T(m.Key));
            b.AddThemeColorOverride("font_color", m.Key == _metric ? Overlay.Accent : Overlay.Muted);
            var key = m.Key;
            b.Pressed += () => Capture.Safe("CombatSummary.Metric", () =>
            {
                _metric = key;
                Rebuild(combat);
            });
            tabs.AddChild(b);
        }
        tabs.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var mode = Overlay.MakeButton(Loc.T(Cfg.ChartPerRound ? "summary.perRound" : "summary.cumulative"));
        mode.AddThemeColorOverride("font_color", Overlay.Accent);
        mode.TooltipText = Loc.T("summary.modeTip");
        mode.Pressed += () => Capture.Safe("CombatSummary.Mode", () =>
        {
            Cfg.ChartPerRound = !Cfg.ChartPerRound;
            Cfg.Save();
            Rebuild(combat);
        });
        tabs.AddChild(mode);
        _content.AddChild(tabs);

        var metric = Metrics.FirstOrDefault(m => m.Key == _metric && (coop || !m.CoopOnly)) ?? Metrics[0];
        var body = new HBoxContainer();
        body.AddThemeConstantOverride("separation", 14);
        var left = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        left.AddChild(BuildChart(combat, metric));
        left.AddChild(BuildLegend(combat, metric));
        body.AddChild(left);
        if (coop) body.AddChild(BuildCoopSummary(combat));
        _content.AddChild(body);

        var auto = new CheckBox { Text = Loc.T("summary.auto"), ButtonPressed = Cfg.AutoSummary, FocusMode = Control.FocusModeEnum.None };
        auto.AddThemeFontSizeOverride("font_size", Overlay.FontSize);
        auto.AddThemeColorOverride("font_color", Overlay.Muted);
        auto.Toggled += on => Capture.Safe("CombatSummary.Auto", () =>
        {
            Cfg.AutoSummary = on;
            Cfg.Save();
        });
        _content.AddChild(auto);
        _panel!.CallDeferred(Control.MethodName.ResetSize);
    }

    /// "Combat 4 · Jaw Worm · Victory · 5 rounds" or "▶ Combat 5 · Cultist · in progress (round 2)".
    internal static string Label(CombatRecord c)
    {
        var name = c.Name == "" ? "" : $" · {c.Name}";
        return c.InProgress
            ? $"▶ {Loc.T("summary.title")} {c.Number}{name} · {Loc.T("summary.inProgress")} ({Loc.T("summary.round").ToLowerInvariant()} {c.LastRound})"
            : $"{Loc.T("summary.title")} {c.Number}{name} · {Loc.T(c.Victory == true ? "summary.victory" : "summary.defeat")} · {c.LastRound} {Loc.T("summary.rounds")}";
    }

    private static List<ulong> Players(CombatRecord combat) =>
        combat.Rounds.SelectMany(r => r.Players.Keys).Distinct().OrderBy(id => id).ToList();

    private static Control BuildChart(CombatRecord combat, Metric metric)
    {
        // Pass: dragging on the chart moves the window too.
        var chart = new Control { CustomMinimumSize = new Vector2(ChartWidth, ChartHeight), MouseFilter = Control.MouseFilterEnum.Pass };
        var plotW = ChartWidth - AxisLeft - 10;
        var plotH = ChartHeight - AxisBottom - 8;
        var lastRound = Math.Max(1, combat.LastRound);
        var points = Points(combat, metric);
        var (max, yStep) = Axis(points.SelectMany(p => p.Values.Values).DefaultIfEmpty(0).Max());

        float X(int round) => AxisLeft + plotW * round / lastRound;
        float Y(double v) => 8 + plotH * (float)(1 - v / max);

        // Horizontal grid lines with their values: whole, distinct steps.
        var lines = (int)Math.Round(max / yStep);
        for (var i = 0; i <= lines; i++)
        {
            var v = yStep * i;
            chart.AddChild(new ColorRect
            {
                Color = new Color(1, 1, 1, i == 0 ? 0.35f : 0.08f),
                Position = new Vector2(AxisLeft, Y(v)), Size = new Vector2(plotW, 1), MouseFilter = Control.MouseFilterEnum.Ignore
            });
            var label = Overlay.MakeLabel(Overlay.N(v), Overlay.Muted, HorizontalAlignment.Right);
            label.Position = new Vector2(0, Y(v) - 9);
            label.Size = new Vector2(AxisLeft - 4, 18);
            chart.AddChild(label);
        }

        // Round numbers on the x axis (at most ~10 labels).
        var step = Math.Max(1, (int)Math.Ceiling(lastRound / 10.0));
        for (var r = 0; r <= lastRound; r += step)
        {
            var label = Overlay.MakeLabel(r.ToString(CultureInfo.InvariantCulture), Overlay.Muted, HorizontalAlignment.Center);
            label.Position = new Vector2(X(r) - 15, ChartHeight - AxisBottom + 2);
            label.Size = new Vector2(30, 18);
            chart.AddChild(label);
        }

        // One line per player, with a dot at each round.
        foreach (var (id, color) in PlayerColors(combat))
        {
            var line = new Line2D { Width = 2, DefaultColor = color, Antialiased = true };
            foreach (var (round, values) in points)
            {
                if (!values.TryGetValue(id, out var value)) continue;
                var p = new Vector2(X(round), Y(value));
                line.AddPoint(p);
                chart.AddChild(new ColorRect
                {
                    Color = color, Position = p - new Vector2(2.5f, 2.5f), Size = new Vector2(5, 5),
                    TooltipText = $"{Loc.T("summary.round")} {round}: {Overlay.N(value)}"
                });
            }
            chart.AddChild(line);
        }
        return chart;
    }

    /// Each round's value per player: the cumulative total, or (per round) what was added during that round.
    private static List<(int Round, Dictionary<ulong, double> Values)> Points(CombatRecord combat, Metric metric)
    {
        var result = new List<(int, Dictionary<ulong, double>)>();
        RoundPoint? previous = null;
        foreach (var point in combat.Rounds)
        {
            var values = point.Players.ToDictionary(kv => kv.Key, kv =>
            {
                var v = metric.Value(kv.Value);
                if (!Cfg.ChartPerRound) return v;
                return previous != null && previous.Players.TryGetValue(kv.Key, out var before) ? v - metric.Value(before) : v;
            });
            if (!Cfg.ChartPerRound || point.Round > 0) result.Add((point.Round, values));
            previous = point;
        }
        return result;
    }

    /// Co-op summary of the combat, next to the chart: what each player did for the others (best first).
    private static Control BuildCoopSummary(CombatRecord combat)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        box.AddChild(Overlay.MakeLabel(Loc.T("section.coop"), Overlay.Accent));
        var columns = new (string Key, Func<PlayerStats, double> Value)[]
        {
            ("col.blockGiven", s => s.BlockGivenToAllies), ("col.saved", s => s.SavedForAllies),
            ("col.enabled", s => s.DamageEnabledAllies), ("col.friendly", s => s.FriendlyDamage), ("col.deaths", s => s.Deaths),
        };
        var grid = new GridContainer { Columns = columns.Length + 1, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddChild(Overlay.MakeLabel("", Overlay.Muted));
        foreach (var (key, _) in columns)
        {
            var l = Overlay.MakeLabel(Loc.T(key), Overlay.Muted, HorizontalAlignment.Right);
            if (Loc.Has($"tip.{key}"))
            {
                l.TooltipText = Loc.T($"tip.{key}");
                l.MouseFilter = Control.MouseFilterEnum.Stop;
            }
            grid.AddChild(l);
        }
        foreach (var (id, s) in combat.Stats.OrderByDescending(kv => kv.Value.SavedForAllies + kv.Value.DamageEnabledAllies).ThenBy(kv => kv.Key))
        {
            var name = Capture.Engine.Players.GetValueOrDefault(id)?.Name ?? id.ToString(CultureInfo.InvariantCulture);
            var n = Overlay.MakeLabel(name, Overlay.PlayerColor(id));
            n.ClipText = true;
            n.CustomMinimumSize = new Vector2(80, 0);
            grid.AddChild(n);
            foreach (var (_, value) in columns) grid.AddChild(Overlay.MakeLabel(Overlay.N(value(s)), Overlay.Text, HorizontalAlignment.Right));
        }
        box.AddChild(grid);
        return box;
    }

    /// Moves the window out of the overlay panel's way (the panel grows when sections open). Not saved.
    private static void KeepClear()
    {
        if (_panel is not { Visible: true } || _dragging || Overlay.PanelRect is not { } overlay) return;
        var size = _panel.Size;
        var rect = new Rect2(_panel.Position, size);
        if (!rect.Intersects(overlay)) return;
        var screen = _panel.GetViewportRect().Size;
        var y = Math.Clamp(_panel.Position.Y, 0, Math.Max(0, screen.Y - size.Y));
        if (overlay.End.X + 10 + size.X <= screen.X) _panel.Position = new Vector2(overlay.End.X + 10, y);
        else if (overlay.Position.X - 10 - size.X >= 0) _panel.Position = new Vector2(overlay.Position.X - 10 - size.X, y);
        else _panel.Position = new Vector2(_panel.Position.X, Math.Min(screen.Y - size.Y, overlay.End.Y + 10));
    }

    private static Control BuildLegend(CombatRecord combat, Metric metric)
    {
        var legend = new HFlowContainer();
        legend.AddThemeConstantOverride("h_separation", 16);
        var last = combat.Rounds.Count > 0 ? combat.Rounds[^1] : null;
        foreach (var (id, color) in PlayerColors(combat))
        {
            var name = Capture.Engine.Players.GetValueOrDefault(id)?.Name ?? id.ToString(CultureInfo.InvariantCulture);
            var total = last != null && last.Players.TryGetValue(id, out var t) ? metric.Value(t) : 0;
            legend.AddChild(Overlay.MakeLabel($"■ {name}: {Overlay.N(total)}", color));
            legend.MouseFilter = Control.MouseFilterEnum.Pass;
        }
        return legend;
    }

    /// Character colors; a second player with the same character gets a lighter shade (as in the panel).
    private static List<(ulong Id, Color Color)> PlayerColors(CombatRecord combat) =>
        Players(combat).Select(id => (id, Overlay.PlayerColor(id))).ToList();

    /// The axis: a step of 1, 2 or 5 × 10^k (never below 1, so labels are whole and distinct) giving at most 4
    /// intervals, and the maximum = the value rounded up to a step (never 0).
    private static (double Max, double Step) Axis(double v)
    {
        var target = Math.Max(1, v / 4);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        var step = new[] { 1, 2, 5, 10 }.Select(f => f * magnitude).First(x => x >= target - 1e-9);
        return (Math.Max(step, Math.Ceiling(v / step - 1e-9) * step), step);
    }
}
