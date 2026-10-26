using System.Globalization;
using Godot;
using KikiMAIster.Core;

namespace KikiMAIster.UI;

/// Combat summary (spec 02-ui §2): a cumulative chart per round, one line per player, for a chosen stat.
/// A selector picks the combat: the one in progress (updated live) or any finished combat of the run.
/// Opens by itself when a combat ends; the overlay's "Summary" button opens it at any time.
public static class CombatSummary
{
    private sealed record Metric(string Key, Func<RoundTotals, double> Value);

    private static readonly Metric[] Metrics =
    {
        new("summary.damage", t => t.Damage),
        new("summary.prevented", t => t.Prevented),
        new("summary.hpLost", t => t.HpLost),
        new("summary.debuffs", t => t.DebuffStacks),
    };

    private const float ChartWidth = 420, ChartHeight = 200, AxisLeft = 40, AxisBottom = 20;

    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static int _shownCombat;
    private static int _selected;
    private static string _liveSignature = "";
    private static bool _rebuildPending;
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
            _selected = last.Number;
            Show();
            return;
        }
        if (_panel is not { Visible: true } || Selected() is not { } selected) return;
        if (_rebuildPending || (selected.InProgress && Signature(selected) != _liveSignature))
        {
            _rebuildPending = false;
            Rebuild(selected);
        }
    }

    public static void Toggle()
    {
        if (_panel == null) return;
        if (_panel.Visible)
        {
            _panel.Visible = false;
            return;
        }
        // Opens on the combat in progress, else on the last finished one.
        _selected = (Capture.Engine.CurrentCombat ?? Capture.Engine.LastCombat)?.Number ?? 0;
        Show();
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
            $"{kv.Value.Damage:F0}/{kv.Value.Prevented:F0}/{kv.Value.HpLost:F0}/{kv.Value.DebuffStacks:F0}"));

    private static void Show()
    {
        if (_panel == null || _content == null || Selected() is not { } combat) return;
        Rebuild(combat);
        _panel.Visible = true;
        _panel.CallDeferred(Control.MethodName.ResetSize);
        // Top center of the screen, above the rewards.
        var screen = _panel.GetViewportRect().Size;
        _panel.Position = new Vector2(Math.Max(0, (screen.X - ChartWidth - 40) / 2), 70);
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
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("CombatSummary.Close", () => _panel!.Visible = false);
        header.AddChild(close);
        _content.AddChild(header);

        // Stat selector.
        var tabs = new HBoxContainer();
        foreach (var m in Metrics)
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
        _content.AddChild(tabs);

        var metric = Metrics.First(m => m.Key == _metric);
        _content.AddChild(BuildChart(combat, metric));
        _content.AddChild(BuildLegend(combat, metric));
        _panel!.CallDeferred(Control.MethodName.ResetSize);
    }

    /// "Combat 4 · Jaw Worm · Victory · 5 rounds" or "▶ Combat 5 · Cultist · in progress (round 2)".
    private static string Label(CombatRecord c)
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
        var chart = new Control { CustomMinimumSize = new Vector2(ChartWidth, ChartHeight) };
        var plotW = ChartWidth - AxisLeft - 10;
        var plotH = ChartHeight - AxisBottom - 8;
        var lastRound = Math.Max(1, combat.LastRound);
        var max = NiceMax(combat.Rounds.SelectMany(r => r.Players.Values).Select(metric.Value).DefaultIfEmpty(0).Max());

        float X(int round) => AxisLeft + plotW * round / lastRound;
        float Y(double v) => 8 + plotH * (float)(1 - v / max);

        // Horizontal grid lines with their values.
        for (var i = 0; i <= 4; i++)
        {
            var v = max * i / 4;
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
            foreach (var point in combat.Rounds)
            {
                if (!point.Players.TryGetValue(id, out var totals)) continue;
                var p = new Vector2(X(point.Round), Y(metric.Value(totals)));
                line.AddPoint(p);
                chart.AddChild(new ColorRect
                {
                    Color = color, Position = p - new Vector2(2.5f, 2.5f), Size = new Vector2(5, 5),
                    TooltipText = $"{Loc.T("summary.round")} {point.Round}: {Overlay.N(metric.Value(totals))}"
                });
            }
            chart.AddChild(line);
        }
        return chart;
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
        }
        return legend;
    }

    /// Character colors; a second player with the same character gets a lighter shade.
    private static List<(ulong Id, Color Color)> PlayerColors(CombatRecord combat)
    {
        var used = new Dictionary<string, int>();
        var result = new List<(ulong, Color)>();
        foreach (var id in Players(combat))
        {
            var character = Capture.Engine.Players.GetValueOrDefault(id)?.Character ?? "";
            var n = used.GetValueOrDefault(character);
            used[character] = n + 1;
            result.Add((id, Overlay.CharacterColor(character).Lightened(0.3f * n)));
        }
        return result;
    }

    /// Rounds the axis maximum up to 1, 2 or 5 × 10^k (never 0).
    private static double NiceMax(double v)
    {
        if (v <= 0) return 10;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var f in new[] { 1, 2, 5, 10 })
            if (f * magnitude >= v) return f * magnitude;
        return 10 * magnitude;
    }
}
