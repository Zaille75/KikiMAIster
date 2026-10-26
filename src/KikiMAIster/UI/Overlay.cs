using System.Globalization;
using Godot;
using KikiMAIster.Core;

namespace KikiMAIster.UI;

/// Live in-combat panel (spec 02-ui §1). Built only from standard Godot nodes, driven by a timer:
/// a mod cannot register its own Godot script classes.
public static class Overlay
{
    /// Columns: the column keys to show, computed at each refresh (some depend on the data or on solo/co-op).
    private sealed record Section(string Key, Func<StatsEngine, ulong, PlayerStats, IReadOnlyList<Cell>> Row, Func<IReadOnlyList<string>> Columns);

    /// One table cell: its column key, text and optional tooltip.
    private sealed record Cell(string Column, string Text, string Tooltip = "");

    /// Columns about allies, hidden in solo.
    private static readonly HashSet<string> AllyColumns = new() { "col.forAllies", "col.toAllies" };

    private const int FontSize = 13;
    private static readonly Color Text = new(0.93f, 0.9f, 0.82f);
    private static readonly Color Muted = new(0.65f, 0.62f, 0.55f);
    private static readonly Color Accent = new(0.95f, 0.78f, 0.35f);

    private static CanvasLayer? _layer;
    private static PanelContainer? _panel;
    private static VBoxContainer? _body;
    private static readonly Dictionary<string, Button> ScopeButtons = new();
    private static readonly HashSet<string> Expanded = new();
    private static IReadOnlyList<string> _damageColumns = Array.Empty<string>();
    private static IReadOnlyList<string> _debuffColumns = Array.Empty<string>();
    private static bool _solo;
    private static Button? _langButton;
    private static Label? _title;
    private static bool _dragging;
    private static string _lastSignature = "";

    private static readonly Section[] Sections =
    {
        new("damage", DamageRow, () => new[] { "col.total" }.Concat(_damageColumns).ToList()),
        new("protection", ProtectionRow, () => Fixed("col.prevented", "col.forAllies", "col.wasted", "col.useless")),
        new("debuffs", DebuffRow, () => _debuffColumns.Count > 0 ? _debuffColumns : new[] { "col.noDebuff" }),
        new("taken", TakenRow, () => Fixed("col.hpLost", "col.blocked", "col.debuffs")),
        new("friendly", FriendlyRow, () => Fixed("col.toAllies", "col.self", "col.debuffs")),
    };

    private static ModConfig Cfg => ModConfig.Current;

    private static Scope CurrentScope => Enum.TryParse<Scope>(Cfg.Scope, out var s) ? s : Scope.Combat;

    private static bool IsAttached => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.IsInsideTree();

    /// Attaches the overlay if it is not there yet. Safe to call often, from the main thread only.
    public static void EnsureAttached(string reason)
    {
        if (IsAttached) return;
        var game = MegaCrit.Sts2.Core.Nodes.NGame.Instance;
        if (game == null || !GodotObject.IsInstanceValid(game) || !game.IsInsideTree()) return;
        ModLog.Info($"attaching overlay ({reason})");
        Attach(game);
    }

    public static void Attach(Node root)
    {
        if (IsAttached) return;

        _layer = new CanvasLayer { Layer = 90, Name = "KikiMAIsterOverlay" };
        _panel = new PanelContainer { Position = new Vector2(Cfg.X, Cfg.Y), Scale = Vector2.One * Cfg.Scale };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.08f, Cfg.Opacity),
            BorderColor = new Color(Accent, 0.5f),
            BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6
        });

        var outer = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        outer.AddThemeConstantOverride("separation", 4);
        outer.AddChild(BuildHeader());
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 2);
        outer.AddChild(_body);
        _panel.AddChild(outer);
        _layer.AddChild(_panel);
        _panel.Visible = Cfg.Visible;

        var timer = new Godot.Timer { WaitTime = 0.3, Autostart = true };
        timer.Timeout += () => Capture.Safe("Overlay.Refresh", Refresh);
        _layer.AddChild(timer);

        root.AddChild(_layer);
        Refresh();
        ModLog.Info($"overlay attached (inside tree: {_layer.IsInsideTree()}, visible: {_panel.Visible})");
    }

    public static void Toggle()
    {
        EnsureAttached("F7");
        if (_panel == null || !GodotObject.IsInstanceValid(_panel)) return;
        _panel.Visible = !_panel.Visible;
        Cfg.Visible = _panel.Visible;
        Cfg.Save();
    }

    // ---------- Building ----------

    private static Control BuildHeader()
    {
        var header = new HBoxContainer();
        var title = MakeLabel("KikiMAIster", Accent);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Stop;
        title.MouseDefaultCursorShape = Control.CursorShape.Move;
        title.TooltipText = Loc.T("hint");
        title.GuiInput += OnTitleInput;
        header.AddChild(title);
        _title = title;

        foreach (var scope in Enum.GetValues<Scope>())
        {
            var b = MakeButton(Loc.T($"scope.{scope}"));
            b.Pressed += () => Capture.Safe("Overlay.Scope", () =>
            {
                Cfg.Scope = scope.ToString();
                Cfg.Save();
                _lastSignature = "";
                Refresh();
            });
            ScopeButtons[scope.ToString()] = b;
            header.AddChild(b);
        }

        _langButton = MakeButton("");
        _langButton.TooltipText = Loc.T("lang.tip");
        _langButton.Pressed += () => Capture.Safe("Overlay.Language", () =>
        {
            Cfg.Language = Loc.French ? "en" : "fr";
            Cfg.Save();
            _lastSignature = "";
            Refresh();
        });
        header.AddChild(_langButton);

        var close = MakeButton("✕");
        close.Pressed += Toggle;
        header.AddChild(close);
        return header;
    }

    private static void OnTitleInput(InputEvent e)
    {
        if (_panel == null) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                if (!mb.Pressed)
                {
                    Cfg.X = _panel.Position.X;
                    Cfg.Y = _panel.Position.Y;
                    Cfg.Save();
                }
                break;
            case InputEventMouseMotion mm when _dragging:
                _panel.Position += mm.Relative;
                break;
        }
    }

    // ---------- Refresh ----------

    private static void Refresh()
    {
        if (_panel == null || _body == null || !_panel.Visible) return;

        foreach (var (key, b) in ScopeButtons)
        {
            b.Text = Loc.T($"scope.{key}");
            b.AddThemeColorOverride("font_color", key == Cfg.Scope ? Accent : Muted);
        }
        if (_langButton != null)
        {
            _langButton.Text = Loc.French ? "FR" : "EN";
            _langButton.TooltipText = Loc.T("lang.tip");
        }
        if (_title != null) _title.TooltipText = Loc.T("hint");

        var engine = Capture.Engine;
        var stats = engine.Get(CurrentScope);

        // Rebuild only when something changed (cheap check on the displayed numbers).
        var signature = Signature(engine, stats);
        if (signature == _lastSignature) return;
        _lastSignature = signature;

        // Remove old rows right away (QueueFree alone keeps them until the end of the frame), then let the
        // panel shrink back to its content: containers grow on their own but never shrink.
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
        _panel.CallDeferred(Control.MethodName.ResetSize);

        if (stats.Count == 0)
        {
            _body.AddChild(MakeLabel(Loc.T("empty"), Muted));
            return;
        }

        var players = stats.Keys.OrderBy(id => id).ToList();
        _damageColumns = DamageColumns(stats);
        _debuffColumns = stats.Values.SelectMany(x => x.DebuffsApplied)
            .GroupBy(kv => kv.Key).OrderByDescending(g => g.Sum(kv => kv.Value.Stacks)).Select(g => g.Key).ToList();
        _solo = players.Count == 1;
        foreach (var section in Sections)
        {
            var open = Cfg.OpenSections.Contains(section.Key);
            var header = MakeButton($"{(open ? "▾" : "▸")} {Loc.T($"section.{section.Key}")}");
            header.Alignment = HorizontalAlignment.Left;
            header.AddThemeColorOverride("font_color", Accent);
            var key = section.Key;
            void Fold() => Capture.Safe("Overlay.Fold", () =>
            {
                if (!Cfg.OpenSections.Remove(key)) Cfg.OpenSections.Add(key);
                Cfg.Save();
                _lastSignature = "";
                Refresh();
            });
            header.Pressed += Fold;

            if (players.Count == 1)
            {
                // Solo: the table is always shown, without the name column; opening the section adds its details.
                var id = players[0];
                var cells = section.Row(engine, id, stats[id]);
                _body.AddChild(BuildSoloTable(section, header, cells, Fold));
                var detail = cells.FirstOrDefault(c => c.Tooltip != "")?.Tooltip ?? "";
                if (open && detail != "") _body.AddChild(BuildDetail(null, Accent, detail));
                continue;
            }
            _body.AddChild(header);
            if (open) _body.AddChild(BuildTable(section, engine, stats, players));
        }
    }

    private static IReadOnlyList<string> Fixed(params string[] columns) =>
        _solo ? columns.Where(c => !AllyColumns.Contains(c)).ToList() : columns;

    private static Label HeaderLabel(Section section, string column)
    {
        var l = MakeLabel(ColumnName(column), Muted, HorizontalAlignment.Right);
        if (section.Key == "debuffs" && column != "col.noDebuff")
        {
            l.TooltipText = Loc.T("tip.debuffXY");
            l.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        else if (Loc.Has($"tip.{column}"))
        {
            l.TooltipText = Loc.T($"tip.{column}");
            l.MouseFilter = Control.MouseFilterEnum.Stop;
        }
        return l;
    }

    private static Cell CellFor(IReadOnlyList<Cell> cells, string column) =>
        cells.FirstOrDefault(c => c.Column == column) ?? new Cell(column, "-", cells.FirstOrDefault()?.Tooltip ?? "");

    /// Solo: the section title sits on the column-name line, the values on the line below.
    private static Control BuildSoloTable(Section section, Button title, IReadOnlyList<Cell> cells, Action fold)
    {
        var columns = section.Columns();
        var grid = new GridContainer { Columns = columns.Count + 1 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 0);
        title.CustomMinimumSize = new Vector2(130, 0);
        grid.AddChild(title);
        foreach (var col in columns) grid.AddChild(HeaderLabel(section, col));
        grid.AddChild(MakeLabel("", Muted));
        foreach (var col in columns)
        {
            var cell = CellFor(cells, col);
            var c = MakeLabel(cell.Text, Text, HorizontalAlignment.Right);
            c.MouseFilter = Control.MouseFilterEnum.Stop;
            c.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            c.TooltipText = cell.Tooltip == "" ? Loc.T("row.tip") : cell.Tooltip + "\n\n" + Loc.T("row.tip");
            c.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) fold();
            };
            grid.AddChild(c);
        }
        return grid;
    }

    private static Control BuildTable(Section section, StatsEngine engine, IReadOnlyDictionary<ulong, PlayerStats> stats, List<ulong> players)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        var details = new List<Control>();
        var columns = section.Columns();
        var grid = new GridContainer { Columns = columns.Count + 1 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 1);

        grid.AddChild(MakeLabel("", Muted));
        foreach (var col in columns) grid.AddChild(HeaderLabel(section, col));

        foreach (var id in players)
        {
            var info = engine.Players.GetValueOrDefault(id);
            var name = MakeLabel(info?.Name ?? id.ToString(), CharacterColor(info?.Character));
            name.CustomMinimumSize = new Vector2(90, 0);
            name.ClipText = true;
            var cells = section.Row(engine, id, stats[id]);
            var detail = cells.FirstOrDefault(c => c.Tooltip != "")?.Tooltip ?? "";
            var rowKey = $"{section.Key}:{id}";
            MakeClickable(name, rowKey, detail);
            grid.AddChild(name);
            foreach (var col in columns)
            {
                var cell = CellFor(cells, col);
                Control c = MakeLabel(cell.Text, Text, HorizontalAlignment.Right);
                MakeClickable(c, rowKey, cell.Tooltip);
                grid.AddChild(c);
            }

            if (detail != "" && Expanded.Contains(rowKey))
                details.Add(BuildDetail(info?.Name ?? id.ToString(), CharacterColor(info?.Character), detail));
        }
        box.AddChild(grid);
        foreach (var d in details) box.AddChild(d);
        return box;
    }

    /// Hovering a row shows its details as a tooltip; clicking it opens them inside the panel.
    private static void MakeClickable(Control c, string rowKey, string tooltip)
    {
        c.MouseFilter = Control.MouseFilterEnum.Stop;
        c.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        c.TooltipText = tooltip == "" ? Loc.T("row.tip") : tooltip + "\n\n" + Loc.T("row.tip");
        c.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
            Capture.Safe("Overlay.Expand", () =>
            {
                if (!Expanded.Remove(rowKey)) Expanded.Add(rowKey);
                _lastSignature = "";
                Refresh();
            });
        };
    }

    private static Control BuildDetail(string? player, Color color, string text)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0.05f),
            BorderColor = new Color(color, 0.6f), BorderWidthLeft = 2,
            ContentMarginLeft = 8, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2
        });
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 0);
        if (player != null) v.AddChild(MakeLabel(player, color));
        v.AddChild(MakeLabel(text, Text));
        panel.AddChild(v);
        return panel;
    }

    // ---------- Rows (one per section) ----------

    private static IReadOnlyList<Cell> DamageRow(StatsEngine e, ulong id, PlayerStats s)
    {
        var total = e.Get(CurrentScope).Values.Sum(x => x.DamageDealt);
        var share = total > 0 ? s.DamageDealt / total : 0;
        var tip = Tip(
            ("tip.intoBlock", N(s.DamageIntoBlock), false), ("tip.overkill", N(s.Overkill), false),
            ("tip.kills", N(s.Kills), false), ("tip.biggestHit", N(s.BiggestHit), false))
            + "\n\n" + Loc.T("tip.bySource") + ":\n" + Lines(s.DamageBySource);
        var byColumn = s.DamageBySource.GroupBy(kv => DamageColumn(kv.Key)).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        var totalText = _solo ? N(s.DamageDealt) : $"{N(s.DamageDealt)} ({share:P0})";
        return new[] { new Cell("col.total", totalText, tip) }
            .Concat(_damageColumns.Select(c => new Cell(c, N(byColumn.GetValueOrDefault(c)), tip))).ToArray();
    }

    /// Damage columns: cards, then each damage-over-time debuff by name (Poison, Doom…), then the other kinds.
    /// Only kinds that dealt damage in the current scope are shown.
    private static IReadOnlyList<string> DamageColumns(IReadOnlyDictionary<ulong, PlayerStats> stats)
    {
        var totals = stats.Values.SelectMany(s => s.DamageBySource)
            .GroupBy(kv => DamageColumn(kv.Key)).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        var order = new[] { "cat.cards", "dot", "cat.powers", "cat.pet", "cat.orbs", "cat.relics", "cat.potions", "cat.other" };
        return totals.Where(kv => kv.Value >= 0.5)
            .OrderBy(kv => Array.IndexOf(order, kv.Key.StartsWith("DamageOverTime:") ? "dot" : kv.Key))
            .ThenByDescending(kv => kv.Value)
            .Select(kv => kv.Key).ToList();
    }

    private static string DamageColumn(string sourceKey)
    {
        if (sourceKey.StartsWith("DamageOverTime:")) return sourceKey;
        var kind = sourceKey.Split(':')[0];
        return kind switch
        {
            "Card" => "cat.cards",
            "Power" => "cat.powers",
            "Pet" => "cat.pet",
            "Orb" => "cat.orbs",
            "Relic" => "cat.relics",
            "Potion" => "cat.potions",
            _ => "cat.other"
        };
    }

    private static string ColumnName(string column) =>
        column.StartsWith("cat.") || column.StartsWith("col.") ? Loc.T(column) : GameNames.Of(column);

    private static IReadOnlyList<Cell> ProtectionRow(StatsEngine e, ulong id, PlayerStats s)
    {
        var tip = Tip(
            ("tip.blockSelf", N(s.BlockGainedSelf), false), ("tip.blockAllies", N(s.BlockGivenToAllies), true),
            ("tip.preventedOwn", N(s.PreventedOwnBlock), false), ("tip.preventedAllies", N(s.PreventedForAllies), true),
            ("tip.col.wasted", N(s.WastedBlock), false), ("tip.col.useless", N(s.UselessBlock), false),
            ("tip.unusedAllies", N(s.UnusedGivenToAllies), true), ("tip.spent", N(s.BlockSpent), false),
            ("tip.pet", N(s.AbsorbedByPet), false), ("tip.petDeaths", N(s.PetDeaths), false),
            ("tip.healSelf", N(s.HealingSelf), false), ("tip.healAllies", N(s.HealingAllies), true));
        return new[]
        {
            new Cell("col.prevented", N(s.PreventedOwnBlock + s.PreventedForAllies + s.AbsorbedByPet), tip),
            new Cell("col.forAllies", N(s.PreventedForAllies), tip),
            new Cell("col.wasted", N(s.WastedBlock), tip),
            new Cell("col.useless", N(s.UselessBlock), tip)
        };
    }

    /// One column per debuff: "x/y" = times applied / total stacks.
    private static IReadOnlyList<Cell> DebuffRow(StatsEngine e, ulong id, PlayerStats s)
    {
        var tip = (s.DebuffsApplied.Count == 0 ? Loc.T("tip.none") : Powers(s.DebuffsApplied))
                  + "\n\n" + Loc.T("tip.debuffXY") + "\n" + Loc.T("tip.dot") + ": " + N(s.DotDamage);
        return s.DebuffsApplied.Select(kv => new Cell(kv.Key, $"{kv.Value.Count}/{N(kv.Value.Stacks)}", tip))
            .Append(new Cell("col.noDebuff", "-", tip)).ToArray();
    }

    private static IReadOnlyList<Cell> TakenRow(StatsEngine e, ulong id, PlayerStats s)
    {
        var tip = Tip(
            ("tip.fromEnemies", N(s.HpLostFromEnemies), false), ("tip.fromAllies", N(s.HpLostFromAllies), true),
            ("tip.fromSelf", N(s.HpLostFromSelf), false), ("tip.other", N(s.HpLostOther), false))
            + (s.DebuffsReceived.Count == 0 ? "" : "\n\n" + Powers(s.DebuffsReceived));
        return new[]
        {
            new Cell("col.hpLost", N(s.HpLost), tip),
            new Cell("col.blocked", N(s.DamageBlocked), tip),
            new Cell("col.debuffs", N(s.DebuffsReceived.Values.Sum(t => t.Count)), tip)
        };
    }

    private static IReadOnlyList<Cell> FriendlyRow(StatsEngine e, ulong id, PlayerStats s)
    {
        var tip = s.FriendlyDebuffs.Count == 0 ? Loc.T("tip.none") : Powers(s.FriendlyDebuffs);
        return new[]
        {
            new Cell("col.toAllies", N(s.FriendlyDamage), tip),
            new Cell("col.self", N(s.SelfDamage), tip),
            new Cell("col.debuffs", N(s.FriendlyDebuffs.Values.Sum(t => t.Count)), tip)
        };
    }

    // ---------- Helpers ----------

    private static string Signature(StatsEngine engine, IReadOnlyDictionary<ulong, PlayerStats> stats) =>
        Cfg.Scope + "|" + string.Join(',', Cfg.OpenSections) + "|" + string.Join(',', Expanded) + "|" + Loc.French + "|" + string.Join(';', stats.Select(kv =>
            $"{kv.Key}:{kv.Value.DamageDealt:F1}:{kv.Value.TotalRawDamage:F1}:{kv.Value.PreventedOwnBlock + kv.Value.PreventedForAllies:F1}:" +
            $"{kv.Value.WastedBlock:F1}:{kv.Value.UselessBlock + kv.Value.BlockSpent:F1}:{kv.Value.BlockGainedSelf + kv.Value.BlockGivenToAllies:F1}:" +
            $"{kv.Value.HpLost:F1}:{kv.Value.DamageBlocked:F1}:{kv.Value.DebuffsApplied.Values.Sum(t => t.Stacks):F1}:{kv.Value.DebuffsApplied.Values.Sum(t => t.Count)}:" +
            $"{kv.Value.DebuffsReceived.Count}:{kv.Value.FriendlyDamage + kv.Value.SelfDamage:F1}:{kv.Value.FriendlyDebuffs.Count}:" +
            $"{kv.Value.HealingSelf + kv.Value.HealingAllies:F1}:{kv.Value.AbsorbedByPet:F1}:{kv.Value.Kills}"));

    private static string N(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    /// Tooltip lines "label: value"; lines about allies are left out in solo.
    private static string Tip(params (string LabelKey, string Value, bool Ally)[] lines) =>
        string.Join('\n', lines.Where(l => !(_solo && l.Ally)).Select(l => $"{Loc.T(l.LabelKey)}: {l.Value}"));

    private static string Lines(Dictionary<string, double> map) =>
        map.Count == 0 ? Loc.T("tip.none") : string.Join('\n', map.OrderByDescending(kv => kv.Value).Select(kv => $"  {Pretty(kv.Key)}: {N(kv.Value)}"));

    private static string Powers(Dictionary<string, PowerTally> map) =>
        string.Join('\n', map.OrderByDescending(kv => kv.Value.Stacks).Select(kv => $"{Pretty(kv.Key)}: x{kv.Value.Count} ({N(kv.Value.Stacks)})"));

    private static string Pretty(string key) => GameNames.Of(key);

    private static Color CharacterColor(string? character) => character switch
    {
        "IRONCLAD" => new Color(0.85f, 0.3f, 0.25f),
        "SILENT" => new Color(0.35f, 0.75f, 0.4f),
        "DEFECT" => new Color(0.35f, 0.6f, 0.95f),
        "NECROBINDER" => new Color(0.7f, 0.45f, 0.9f),
        "REGENT" => new Color(0.95f, 0.65f, 0.25f),
        _ => new Color(0.75f, 0.75f, 0.75f)
    };

    private static Label MakeLabel(string text, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeFontSizeOverride("font_size", FontSize);
        return l;
    }

    private static Button MakeButton(string text)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.AddThemeColorOverride("font_color", Text);
        return b;
    }
}
