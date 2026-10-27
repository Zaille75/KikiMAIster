using System.Globalization;
using Godot;
using KikiMAIster.Core;

namespace KikiMAIster.UI;

/// Live in-combat panel (spec 02-ui §1). Built only from standard Godot nodes, driven by a timer:
/// a mod cannot register its own Godot script classes.
public static class Overlay
{
    /// Columns: the column keys to show, computed at each refresh (some depend on the data or on solo/co-op).
    /// Rank: players are listed from the best to the worst by this value (highest first).
    private sealed record Section(string Key, Func<ulong, PlayerStats, IReadOnlyList<Cell>> Row, Func<IReadOnlyList<string>> Columns,
        Func<PlayerStats, double> Rank, bool CoopOnly = false);

    /// One table cell: its column key, text and optional tooltip.
    private sealed record Cell(string Column, string Text, string Tooltip = "");

    /// Columns about allies, hidden in solo.
    private static readonly HashSet<string> AllyColumns = new() { "col.fromAllies" };
    /// The Run tab's "Erreurs" column: Gâché + Choix.
    private const string MisplaysKey = "col.misplays";

    internal const int FontSize = 13;
    internal static readonly Color Text = new(0.93f, 0.9f, 0.82f);
    internal static readonly Color Muted = new(0.65f, 0.62f, 0.55f);
    internal static readonly Color Accent = new(0.95f, 0.78f, 0.35f);

    private static CanvasLayer? _layer;
    private static PanelContainer? _panel;
    private static VBoxContainer? _body;
    private static readonly Dictionary<string, Button> ScopeButtons = new();
    private static readonly HashSet<string> Expanded = new();
    private static IReadOnlyList<string> _damageColumns = Array.Empty<string>();
    private static IReadOnlyList<string> _debuffColumns = Array.Empty<string>();
    private static IReadOnlyDictionary<ulong, PlayerStats> _shown = new Dictionary<ulong, PlayerStats>();
    private static bool _solo;
    private static bool _anyPetHp;
    private static Button? _settingsButton;
    private static Button? _summaryButton;
    private static Label? _title;
    private static bool _dragging;
    private static string _lastSignature = "";
    /// Combat scope: how many combats back from the latest one is shown (0 = the latest, live if in progress).
    private static int _combatsBack;
    private static int _combatCount;

    /// Declared before Hideable, which reads it during static initialization.
    private static readonly string[] MissedColumns =
        { "col.lethal", "col.wastedBlock", "col.choices", "col.unusedEnergy", "col.hpAvoidable", "col.relicTrigger", "col.potionsLeft" };

    /// Damage taken, shown after the damage dealt in the same section (v0.14.0).
    private static readonly string[] TakenColumns = { "col.hpLost", "col.fromAllies" };

    private static readonly Section[] Sections =
    {
        new("damage", DamageRow, () => Visible("damage", new[] { "col.total" }.Concat(_damageColumns).Concat(Fixed(TakenColumns))), s => s.DamageDealt),
        new("protection", ProtectionRow, () => Visible("protection", Fixed("col.prevented", "col.byBlock", "col.byPet", "col.useless")
            .Concat(_anyPetHp ? new[] { "col.petHp" } : Array.Empty<string>())), s => s.PreventedTotal),
        new("debuffs", DebuffRow, () =>
        {
            var cols = Visible("debuffs", _debuffColumns.Append("col.extraDamage").Append("col.avoided"));
            return cols.Count > 0 ? cols : new[] { "col.noDebuff" };
        }, s => s.DebuffsApplied.Values.Sum(t => t.Stacks)),
        new("missed", MissedRow, MissedColumnsShown, s => -(s.MissedTotal * 100 + s.MissedByKind.GetValueOrDefault("energy"))),
        new("coop", CoopRow, () => Visible("coop", new[] { "col.blockGiven", "col.saved", "col.takenForAllies", "col.enabled", "col.energyGiven", "col.cardsGiven", "col.friendly", "col.deaths" }),
            s => s.SavedForAllies + s.DamageEnabledAllies + s.BlockGivenToAllies * 0.01, CoopOnly: true),
        new("other", OtherRow, () => Visible("other", new[] { "col.drawn", "col.played", "col.exhausted", "col.discarded", "col.potions" }),
            s => s.CardsPlayed),
    };

    /// What the settings menu can hide: every section, and its fixed columns (damage kinds and debuff effect included).
    internal static readonly (string Section, string[] Columns)[] Hideable =
    {
        ("damage", new[] { "cat.cards", "cat.dot", "cat.powers", "cat.pet", "cat.orbs", "cat.relics", "cat.potions", "cat.other", "col.hpLost", "col.fromAllies" }),
        ("protection", new[] { "col.byBlock", "col.byPet", "col.useless", "col.petHp" }),
        ("debuffs", new[] { "col.extraDamage", "col.avoided" }),
        ("missed", MissedColumns),
        ("coop", new[] { "col.blockGiven", "col.saved", "col.takenForAllies", "col.enabled", "col.energyGiven", "col.cardsGiven", "col.friendly", "col.deaths" }),
        ("other", new[] { "col.drawn", "col.played", "col.exhausted", "col.discarded", "col.potions" }),
    };

    private static ModConfig Cfg => ModConfig.Current;

    /// The scopes offered in the header ("Turn", the round in progress, is not shown: v0.7.0).
    private static readonly Scope[] ShownScopes = { Scope.PreviousTurn, Scope.Combat, Scope.Run };

    private static Scope CurrentScope =>
        Enum.TryParse<Scope>(Cfg.Scope, out var s) && ShownScopes.Contains(s) ? s
        : s == Scope.Turn ? Scope.PreviousTurn : Scope.Combat;

    private static bool IsAttached => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.IsInsideTree();

    /// The overlay panel's rectangle on screen (the summary window keeps out of it).
    internal static Rect2? PanelRect => _panel is { Visible: true } p && GodotObject.IsInstanceValid(p)
        ? new Rect2(p.Position, p.Size * p.Scale) : null;

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
        _panel.AddThemeStyleboxOverride("panel", WindowStyle(Cfg.Opacity));

        var outer = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        outer.AddThemeConstantOverride("separation", 2);
        outer.AddChild(BuildHeader());
        _body = new VBoxContainer();
        _body.AddThemeConstantOverride("separation", 1);
        outer.AddChild(_body);
        _panel.AddChild(outer);
        _layer.AddChild(_panel);
        _panel.Visible = Cfg.Visible && InRun;

        var timer = new Godot.Timer { WaitTime = 0.3, Autostart = true };
        timer.Timeout += () => Capture.Safe("Overlay.Refresh", Refresh);
        timer.Timeout += () => Capture.Safe("CombatSummary.Check", CombatSummary.ShowIfNewCombatEnded);
        timer.Timeout += () => Capture.Safe("RunSummary.Check", RunSummary.Tick);
        _layer.AddChild(timer);

        CombatSummary.Attach(_layer);
        SettingsMenu.Attach(_layer);
        RunSummary.Attach(_layer);
        root.AddChild(_layer);
        Refresh();
        ModLog.Info($"overlay attached (inside tree: {_layer.IsInsideTree()}, visible: {_panel.Visible})");
        GameNames.ExportOnce();
    }

    /// A run is being played (not the game's start-up nor the main menu): the only time the mod's windows show.
    internal static bool InRun
    {
        get
        {
            var rm = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
            return rm is { IsInProgress: true, IsCleaningUp: false };
        }
    }

    /// F7 / ✕: remembered show/hide choice; the panel itself only shows during a run.
    public static void Toggle()
    {
        EnsureAttached("F7");
        if (_panel == null || !GodotObject.IsInstanceValid(_panel)) return;
        Cfg.Visible = !Cfg.Visible;
        Cfg.Save();
        SyncVisibility();
    }

    /// Shows the panel only in a run (and when not hidden with F7); outside a run every mod window is closed.
    private static void SyncVisibility()
    {
        if (_panel == null) return;
        var inRun = InRun;
        var show = Cfg.Visible && inRun;
        if (_panel.Visible != show)
        {
            _panel.Visible = show;
            if (show) _lastSignature = "";
        }
        if (!inRun)
        {
            CombatSummary.Hide();
            SettingsMenu.Hide();
            RunSummary.Hide();
        }
    }

    /// Forces a rebuild on the next refresh (settings or language changed).
    internal static void Invalidate()
    {
        _lastSignature = "";
        Capture.Safe("Overlay.Refresh", Refresh);
    }

    internal static StyleBoxFlat WindowStyle(float opacity) => new()
    {
        BgColor = new Color(0.05f, 0.05f, 0.08f, opacity),
        BorderColor = new Color(Accent, 0.5f),
        BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
        ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4
    };

    // ---------- Building ----------

    private static Control BuildHeader()
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 0);
        var title = MakeLabel("KikiMAIster", Accent);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Stop;
        title.MouseDefaultCursorShape = Control.CursorShape.Move;
        title.TooltipText = Loc.T("hint");
        title.GuiInput += OnTitleInput;
        header.AddChild(title);
        _title = title;

        foreach (var scope in ShownScopes)
        {
            var b = MakeButton(Loc.T($"scope.{scope}"));
            b.Pressed += () => Capture.Safe("Overlay.Scope", () =>
            {
                Cfg.Scope = scope.ToString();
                Cfg.Save();
                _combatsBack = 0;
                Invalidate();
            });
            ScopeButtons[scope.ToString()] = b;
            header.AddChild(b);
        }

        _summaryButton = MakeButton("");
        _summaryButton.Pressed += () => Capture.Safe("Overlay.Summary", CombatSummary.Toggle);
        header.AddChild(_summaryButton);

        _settingsButton = MakeButton("⚙");
        _settingsButton.Pressed += () => Capture.Safe("Overlay.Settings", SettingsMenu.Toggle);
        header.AddChild(_settingsButton);

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

    /// Keeps the panel on screen: the saved position is where the player put it; when the panel grows past the
    /// right (or bottom) edge it is shown further left (or up), and goes back once it shrinks.
    private static void FitOnScreen()
    {
        if (_panel == null || _dragging) return;
        var screen = _panel.GetViewportRect().Size;
        var size = _panel.Size * _panel.Scale;
        var x = Math.Max(0, Math.Min(Cfg.X, screen.X - size.X));
        var y = Math.Max(0, Math.Min(Cfg.Y, screen.Y - size.Y));
        var target = new Vector2(x, y);
        if (_panel.Position != target) _panel.Position = target;
    }

    /// The combats the Combat scope can show: finished ones, then the one in progress.
    private static List<CombatRecord> CombatList(StatsEngine engine)
    {
        var list = engine.Combats.ToList();
        if (engine.CurrentCombat is { } live) list.Add(live);
        return list;
    }

    private static void Refresh()
    {
        SyncVisibility();
        if (_panel == null || _body == null || !_panel.Visible) return;
        FitOnScreen();

        foreach (var (key, b) in ScopeButtons)
        {
            b.Text = Loc.T($"scope.{key}");
            b.AddThemeColorOverride("font_color", key == CurrentScope.ToString() ? Accent : Muted);
        }
        if (_settingsButton != null) _settingsButton.TooltipText = Loc.T("settings.tip");
        if (_title != null) _title.TooltipText = Loc.T("hint");
        if (_summaryButton != null)
        {
            _summaryButton.Text = Loc.T("summary.button");
            _summaryButton.TooltipText = Loc.T("summary.tip");
            _summaryButton.Visible = CombatSummary.HasData;
        }

        var engine = Capture.Engine;
        var combats = CombatList(engine);
        // A new combat brings the Combat scope back to the latest one.
        if (combats.Count != _combatCount)
        {
            _combatCount = combats.Count;
            _combatsBack = 0;
        }
        _combatsBack = Math.Clamp(_combatsBack, 0, Math.Max(0, combats.Count - 1));
        CombatRecord? shownCombat = CurrentScope == Scope.Combat && combats.Count > 0 ? combats[^(1 + _combatsBack)] : null;
        var stats = shownCombat?.Stats ?? engine.Get(CurrentScope);
        _shown = stats;

        // Rebuild only when something changed (cheap check on the displayed numbers).
        var signature = Signature(stats) + $"|{_combatsBack}/{combats.Count}";
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

        if (shownCombat != null && combats.Count > 1) _body.AddChild(BuildCombatPicker(shownCombat, combats.Count));

        if (stats.Count == 0)
        {
            _body.AddChild(MakeLabel(Loc.T("empty"), Muted));
            return;
        }

        var players = stats.Keys.OrderBy(id => id).ToList();
        _damageColumns = DamageColumns(stats);
        _debuffColumns = stats.Values.SelectMany(x => x.DebuffsApplied)
            .GroupBy(kv => DebuffColumn(kv.Key)).OrderByDescending(g => g.Sum(kv => kv.Value.Stacks)).Select(g => g.Key).ToList();
        _solo = players.Count == 1;
        _anyPetHp = stats.Values.Any(x => x.PetHpGiven > 0);
        foreach (var section in Sections)
        {
            if (Cfg.Hidden.Contains($"section:{section.Key}") || (section.CoopOnly && _solo)) continue;
            var open = Cfg.OpenSections.Contains(section.Key);
            var header = MakeButton($"{(open ? "▾" : "▸")} {Loc.T($"section.{section.Key}")}");
            header.Alignment = HorizontalAlignment.Left;
            header.AddThemeColorOverride("font_color", Accent);
            var key = section.Key;
            void Fold() => Capture.Safe("Overlay.Fold", () =>
            {
                if (!Cfg.OpenSections.Remove(key)) Cfg.OpenSections.Add(key);
                Cfg.Save();
                Invalidate();
            });
            header.Pressed += Fold;

            if (players.Count == 1)
            {
                // Solo: the table is always shown, without the name column; opening the section adds its details.
                var id = players[0];
                var cells = section.Row(id, stats[id]);
                _body.AddChild(BuildSoloTable(section, header, cells, Fold));
                var detail = cells.FirstOrDefault(c => c.Tooltip != "")?.Tooltip ?? "";
                if (open && detail != "") _body.AddChild(BuildDetail(Accent, detail, 320));
                continue;
            }
            if (!open)
            {
                _body.AddChild(header);
                continue;
            }
            var ranked = players.OrderByDescending(id => section.Rank(stats[id])).ThenBy(id => id).ToList();
            _body.AddChild(BuildTable(section, header, engine, stats, ranked));
        }
    }

    /// "◂ Combat 3 · Jaw Worm · Victory ▸": browse the previous combats of the run.
    private static Control BuildCombatPicker(CombatRecord combat, int count)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        var prev = MakeButton("◂");
        prev.Disabled = _combatsBack >= count - 1;
        prev.Pressed += () => Capture.Safe("Overlay.PrevCombat", () => { _combatsBack++; Invalidate(); });
        var next = MakeButton("▸");
        next.Disabled = _combatsBack == 0;
        next.Pressed += () => Capture.Safe("Overlay.NextCombat", () => { _combatsBack--; Invalidate(); });
        var label = MakeLabel(CombatSummary.Label(combat), Muted, HorizontalAlignment.Center);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.ClipText = true;
        label.CustomMinimumSize = new Vector2(200, 0);
        row.AddChild(prev);
        row.AddChild(label);
        row.AddChild(next);
        return row;
    }

    /// Missed chances: in the Run tab, Gâché and Choix are one "Erreurs" column (shown unless both are hidden).
    private static IReadOnlyList<string> MissedColumnsShown()
    {
        if (CurrentScope != Scope.Run) return Visible("missed", MissedColumns);
        var bothHidden = Cfg.Hidden.Contains("col:missed:col.wastedBlock") && Cfg.Hidden.Contains("col:missed:col.choices");
        return MissedColumns.Where(c => c != "col.choices").Select(c => c == "col.wastedBlock" ? MisplaysKey : c)
            .Where(c => c == MisplaysKey ? !bothHidden : !Cfg.Hidden.Contains($"col:missed:{c}")).ToList();
    }

    private static IReadOnlyList<string> Fixed(params string[] columns) =>
        _solo ? columns.Where(c => !AllyColumns.Contains(c)).ToList() : columns;

    /// Leaves out the columns hidden in the settings ("col:<key>"; damage-over-time columns follow "cat.dot").
    private static IReadOnlyList<string> Visible(string section, IEnumerable<string> columns) =>
        columns.Where(c => c == "col.total" || !Cfg.Hidden.Contains($"col:{section}:{(c.StartsWith("DamageOverTime:") ? "cat.dot" : c)}")).ToList();

    private static Label HeaderLabel(Section section, string column)
    {
        var l = MakeLabel(ColumnName(column), Muted, HorizontalAlignment.Right);
        var tip = section.Key == "debuffs" && column.EndsWith("_POWER") || column.Contains("~DOWN") ? Loc.T("tip.debuffXY")
            : Loc.Has($"tip.{column}") ? Loc.T($"tip.{column}") : "";
        if (tip != "")
        {
            l.TooltipText = tip;
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
            c.TooltipText = cell.Tooltip;
            c.GuiInput += e =>
            {
                if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) fold();
            };
            grid.AddChild(c);
        }
        return grid;
    }

    /// Co-op: the section title and the column names share the first line; one line per player, best first.
    /// A player's details open right under their line.
    private static Control BuildTable(Section section, Button title, StatsEngine engine, IReadOnlyDictionary<ulong, PlayerStats> stats,
        List<ulong> players)
    {
        var columns = section.Columns();
        var grid = new GridContainer { Columns = columns.Count + 1 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 0);

        title.CustomMinimumSize = new Vector2(90, 0);
        grid.AddChild(title);
        foreach (var col in columns) grid.AddChild(HeaderLabel(section, col));

        foreach (var id in players)
        {
            var info = engine.Players.GetValueOrDefault(id);
            var color = PlayerColor(id);
            var name = MakeLabel("  " + (info?.Name ?? id.ToString()), color);
            name.CustomMinimumSize = new Vector2(90, 0);
            name.ClipText = true;
            var cells = section.Row(id, stats[id]);
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
            if (detail == "" || !Expanded.Contains(rowKey)) continue;
            // Detail line: the first cell holds the box, drawn over the whole table width; the other cells stay empty.
            grid.AddChild(DetailCell(grid, color, detail));
            for (var i = 0; i < columns.Count; i++) grid.AddChild(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        }
        return grid;
    }

    /// A grid cell of zero width whose content spans the whole grid: its height follows the box inside.
    private static Control DetailCell(GridContainer grid, Color color, string text)
    {
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, 18) };
        var box = BuildDetail(color, text, 320);
        holder.AddChild(box);
        box.Resized += () =>
        {
            if (GodotObject.IsInstanceValid(holder)) holder.CustomMinimumSize = new Vector2(0, box.Size.Y + 2);
        };
        grid.Resized += () =>
        {
            if (GodotObject.IsInstanceValid(box) && Math.Abs(box.CustomMinimumSize.X - grid.Size.X) > 1)
                box.CustomMinimumSize = new Vector2(grid.Size.X, 0);
        };
        return holder;
    }

    /// Hovering a row shows its details as a tooltip; clicking it opens them inside the panel.
    private static void MakeClickable(Control c, string rowKey, string tooltip)
    {
        c.MouseFilter = Control.MouseFilterEnum.Stop;
        c.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        c.TooltipText = tooltip;
        c.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
            Capture.Safe("Overlay.Expand", () =>
            {
                if (!Expanded.Remove(rowKey)) Expanded.Add(rowKey);
                Invalidate();
            });
        };
    }

    private static PanelContainer BuildDetail(Color color, string text, float width)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0.05f),
            BorderColor = new Color(color, 0.6f), BorderWidthLeft = 2,
            ContentMarginLeft = 8, ContentMarginRight = 6, ContentMarginTop = 1, ContentMarginBottom = 1
        });
        var label = MakeLabel(text, Text);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        panel.AddChild(label);
        return panel;
    }

    // ---------- Rows (one per section) ----------

    private static IReadOnlyList<Cell> DamageRow(ulong id, PlayerStats s)
    {
        var total = _shown.Values.Sum(x => x.DamageDealt);
        var share = total > 0 ? s.DamageDealt / total : 0;
        var taken = Line(("tip.fromEnemies", s.HpLostFromEnemies, false), ("tip.fromAllies", s.HpLostFromAllies, true),
                        ("tip.fromSelf", s.HpLostFromSelf, false), ("tip.other", s.HpLostOther, false), ("col.blocked", s.DamageBlocked, false));
        var tip = Line(("tip.intoBlock", s.DamageIntoBlock, false), ("tip.overkill", s.Overkill, false),
                      ("tip.kills", s.Kills, false), ("tip.biggestHit", s.BiggestHit, false), ("tip.bestCard", s.BestCardPlay, false))
                  + Block(Lines(s.DamageBySource))
                  + Block(taken == "" ? "" : $"{Loc.T("col.hpLost")} {N(s.HpLost)}: {taken}")
                  + (s.DebuffsReceived.Count == 0 ? "" : Block($"{Loc.T("tip.debuffsReceived")}:\n{Powers(s.DebuffsReceived)}"));
        var byColumn = s.DamageBySource.GroupBy(kv => DamageColumn(kv.Key)).ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));
        var totalText = _solo ? N(s.DamageDealt) : $"{N(s.DamageDealt)} ({share:P0})";
        return new[] { new Cell("col.total", totalText, tip) }
            .Concat(_damageColumns.Select(c => new Cell(c, N(byColumn.GetValueOrDefault(c)), tip)))
            .Append(new Cell("col.hpLost", N(s.HpLost), tip))
            .Append(new Cell("col.fromAllies", N(s.HpLostFromAllies), tip)).ToArray();
    }

    /// Damage columns: direct (cards), then each damage-over-time debuff by name (Poison, Doom…), then the other kinds.
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

    private static IReadOnlyList<Cell> ProtectionRow(ulong id, PlayerStats s)
    {
        var lines = new List<string>();
        if (s.PreventedByBlock > 0)
            lines.Add(_solo ? $"{Loc.T("tip.byBlock")}: {N(s.PreventedByBlock)}"
                : $"{Loc.T("tip.byBlock")}: {N(s.PreventedByBlock)} ({Loc.T("tip.self")} {N(s.PreventedOwnBlock)} · {Loc.T("tip.allies")} {N(s.PreventedForAllies)})");
        if (s.AbsorbedByPet + s.PetAbsorbedForAllies > 0 || s.PetDeaths > 0)
            lines.Add($"{Loc.T("tip.pet")}: {N(s.AbsorbedByPet + s.PetAbsorbedForAllies)}" +
                      (s.PetAbsorbedForAllies >= 0.5 ? $" ({Loc.T("tip.petOfAllies")} {N(s.PetAbsorbedForAllies)})" : "") +
                      (s.PetDeaths > 0 ? $" · {Loc.T("tip.petDeaths")} {N(s.PetDeaths)}" : ""));
        if (s.PreventedByEffects > 0)
            lines.Add($"{Loc.T("tip.byEffects")}: " + string.Join(", ", s.PreventedByEffectSource.OrderByDescending(kv => kv.Value)
                .Select(kv => $"{GameNames.Of(kv.Key)} {N(kv.Value)}")));
        var byDebuffs = s.PreventedByDebuffsSelf + s.PreventedByDebuffsAllies;
        if (byDebuffs > 0) lines.Add($"{Loc.T("tip.byDebuffs")}: {N(byDebuffs)}");
        if (s.UnusedBlock > 0)
            lines.Add($"{Loc.T("col.useless")}: {N(s.UnusedBlock)}" + (!_solo && s.UnusedGivenToAllies > 0 ? $" ({Loc.T("tip.ofAllies")} {N(s.UnusedGivenToAllies)})" : ""));
        if (s.BlockSpent > 0) lines.Add($"{Loc.T("tip.spent")}: {N(s.BlockSpent)}");
        if (s.PetHpGiven > 0) lines.Add($"{Loc.T("col.petHp")}: {N(s.PetHpGiven)}");
        if (s.HealingSelf > 0) lines.Add($"{Loc.T("tip.healSelf")}: {N(s.HealingSelf)}");
        var tip = string.Join('\n', lines);
        return new[]
        {
            new Cell("col.prevented", N(s.PreventedTotal), tip),
            new Cell("col.byBlock", N(s.PreventedByBlock), tip),
            new Cell("col.byPet", N(s.AbsorbedByPet + s.PetAbsorbedForAllies), tip),
            new Cell("col.useless", N(s.UnusedBlock), tip),
            new Cell("col.petHp", N(s.PetHpGiven), tip)
        };
    }

    /// One column per debuff: "x/y" = times applied / total stacks; then what the debuffs changed: extra damage dealt
    /// (Vulnerable…) and damage avoided (Weak, Strength down…), each debuff's share in the details.
    private static IReadOnlyList<Cell> DebuffRow(ulong id, PlayerStats s)
    {
        var extra = s.DamageEnabledSelf + s.DamageEnabledAllies;
        var avoided = s.PreventedByDebuffsSelf + s.PreventedByDebuffsAllies;
        var tip = (s.DebuffsApplied.Count == 0 ? Loc.T("tip.none") : Powers(s.DebuffsApplied));
        if (s.DotDamage > 0) tip += $"\n{Loc.T("tip.dot")}: {N(s.DotDamage)}";
        if (extra + avoided > 0)
            tip += "\n" + Line(("tip.enabled", extra, false), ("tip.byDebuffs", avoided, false)) + Block(Lines(s.DebuffEffectBySource));
        return s.DebuffsApplied.GroupBy(kv => DebuffColumn(kv.Key))
            .Select(g => new Cell(g.Key, $"{g.Sum(kv => kv.Value.Count)}/{N(g.Sum(kv => kv.Value.Stacks))}", tip))
            .Append(new Cell("col.extraDamage", N(extra), tip))
            .Append(new Cell("col.avoided", N(avoided), tip))
            .Append(new Cell("col.noDebuff", "-", tip)).ToArray();
    }

    /// "STRENGTH_POWER~DOWN@ENFEEBLING_TOUCH_POWER" → "STRENGTH_POWER~DOWN": one column per effect, whatever
    /// card or power caused it; the details (tooltip) keep the origin.
    private static string DebuffColumn(string powerKey)
    {
        var at = powerKey.IndexOf('@');
        return at > 0 ? powerKey[..at] : powerKey;
    }

    /// Missed opportunities, most recent first (at most 3 of each kind).
    private static IReadOnlyList<Cell> MissedRow(ulong id, PlayerStats s)
    {
        var lines = new List<string>();
        foreach (var m in s.MissedLethalTurns.AsEnumerable().Reverse().Take(3))
            lines.Add(string.Format(Loc.T("why.lethal"), At(m.Combat, m.Round), string.Join(" + ", m.Cards.Select(GameNames.Of)), m.EnemiesHp));
        foreach (var w in s.WastedPlays.OrderByDescending(w => w.PlayId).Take(3))
            lines.Add(string.Format(Loc.T("why.wasted"), At(w.Combat, w.Round), w.Choice.IncomingDamage, w.Choice.BlockAtTurnEnd,
                GameNames.Of(w.Choice.BlockCard), N(w.Amount), GameNames.Of(w.Choice.Alternative)));
        foreach (var w in s.WeakAttackPlays.AsEnumerable().Reverse().Take(3))
            lines.Add(string.Format(Loc.T("why.weak"), At(w.Combat, w.Round), GameNames.Of(w.Played), w.PlayedDamage, GameNames.Of(w.Better), w.BetterDamage));
        // Other kinds: the 6 most recent ("T3 · 2 energy left unused"…).
        foreach (var m in s.MissedEntries.AsEnumerable().Reverse().Take(6))
            lines.Add($"{At(m.Combat, m.Round)} · " + string.Format(Loc.T($"why.{m.Kind}"), m.Args.Select(a => (object)(a.Contains(':') ? GameNames.Of(a) : a)).ToArray()));
        var tip = lines.Count == 0 ? (s.MissedTotal > 0 ? "" : Loc.T("tip.none")) : string.Join('\n', lines);
        double K(string kind) => s.MissedByKind.GetValueOrDefault(kind);
        return new[]
        {
            new Cell("col.lethal", N(s.MissedLethals), tip),
            new Cell("col.wastedBlock", N(s.MissedWasted), tip),
            new Cell("col.choices", N(s.MissedChoices), tip),
            new Cell(MisplaysKey, N(s.MissedWasted + s.MissedChoices), tip),
            new Cell("col.unusedEnergy", N(K("energy")), tip),
            new Cell("col.hpAvoidable", N(s.AvoidableHp), tip),
            new Cell("col.relicTrigger", N(K("relic") + K("paelsTears")), tip),
            new Cell("col.potionsLeft", N(K("potion")), tip)
        };
    }

    /// "T9" in a fight's tab, "C3 T9" (fight 3, round 9) in the Run tab.
    private static string At(int combat, int round) =>
        CurrentScope == Scope.Run && combat > 0 ? string.Format(Loc.T("why.atRun"), combat, round) : string.Format(Loc.T("why.at"), round);

    private static IReadOnlyList<Cell> CoopRow(ulong id, PlayerStats s)
    {
        var tip = Line(("tip.preventedAllies", s.PreventedForAllies, false), ("tip.byDebuffs", s.PreventedByDebuffsAllies, false),
                      ("tip.healAllies", s.HealingAllies, false), ("tip.pet", s.PetAbsorbedForAllies, false))
                  + Block(Line(("tip.orbsGiven", s.OrbsGiven, false)))
                  + (s.DebuffEffectBySource.Count > 0 ? Block(Loc.T("tip.helpBy") + ": " + string.Join(", ", s.DebuffEffectBySource
                      .Where(kv => Math.Round(kv.Value) != 0).OrderByDescending(kv => kv.Value).Select(kv => $"{GameNames.Of(kv.Key)} {N(kv.Value)}"))) : "")
                  + (s.FriendlyDamage + s.FriendlyDebuffs.Count > 0
                      ? Block($"{Loc.T("col.friendly")}: {N(s.FriendlyDamage)}" + (s.FriendlyDebuffs.Count > 0 ? "\n" + Powers(s.FriendlyDebuffs) : ""))
                      : "");
        return new[]
        {
            new Cell("col.blockGiven", N(s.BlockGivenToAllies), tip),
            new Cell("col.saved", N(s.SavedForAllies), tip),
            new Cell("col.takenForAllies", N(s.TakenForAllies), tip),
            new Cell("col.enabled", N(s.DamageEnabledAllies), tip),
            new Cell("col.energyGiven", N(s.EnergyGiven), tip),
            new Cell("col.cardsGiven", N(s.CardsGiven), tip),
            new Cell("col.friendly", N(s.FriendlyDamage), tip),
            new Cell("col.deaths", N(s.Deaths), tip)
        };
    }

    private static IReadOnlyList<Cell> OtherRow(ulong id, PlayerStats s) => new[]
    {
        new Cell("col.drawn", N(s.CardsDrawn)), new Cell("col.played", N(s.CardsPlayed)),
        new Cell("col.exhausted", N(s.CardsExhausted)), new Cell("col.discarded", N(s.CardsDiscarded)),
        new Cell("col.potions", N(s.PotionsUsed))
    };

    // ---------- Helpers ----------

    private static string Signature(IReadOnlyDictionary<ulong, PlayerStats> stats) =>
        Cfg.Scope + "|" + string.Join(',', Cfg.OpenSections) + "|" + string.Join(',', Expanded) + "|" + Loc.French + "|" +
        string.Join(',', Cfg.Hidden) + "|" + string.Join(';', stats.Select(kv =>
            $"{kv.Key}:{kv.Value.DamageDealt:F1}:{kv.Value.TotalRawDamage:F1}:{kv.Value.PreventedTotal:F1}:" +
            $"{kv.Value.WastedBlock:F1}:{kv.Value.UselessBlock + kv.Value.BlockSpent:F1}:{kv.Value.BlockGainedSelf + kv.Value.BlockGivenToAllies:F1}:" +
            $"{kv.Value.HpLost:F1}:{kv.Value.DamageBlocked:F1}:{kv.Value.DebuffsApplied.Values.Sum(t => t.Stacks):F1}:{kv.Value.DebuffsApplied.Values.Sum(t => t.Count)}:" +
            $"{kv.Value.DebuffsReceived.Count}:{kv.Value.FriendlyDamage + kv.Value.SelfDamage:F1}:{kv.Value.FriendlyDebuffs.Count}:" +
            $"{kv.Value.HealingSelf + kv.Value.HealingAllies:F1}:{kv.Value.PetHpGiven:F1}:{kv.Value.WastedPlays.Count}:{kv.Value.Kills}:" +
            $"{kv.Value.DamageEnabledSelf + kv.Value.DamageEnabledAllies:F1}:{kv.Value.MissedLethals}:{kv.Value.WeakAttacks}:{kv.Value.EnergyGiven + kv.Value.CardsGiven + kv.Value.OrbsGiven + kv.Value.TakenForAllies:F1}:{kv.Value.MissedEntries.Count}:{kv.Value.Deaths}:" +
            $"{kv.Value.CardsDrawn + kv.Value.CardsPlayed + kv.Value.CardsExhausted + kv.Value.CardsDiscarded + kv.Value.PotionsUsed}"));

    internal static string N(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);

    /// "Label 3 · Label 5" on one line, leaving out zero values (and ally values in solo).
    private static string Line(params (string LabelKey, double Value, bool Ally)[] parts) =>
        string.Join(" · ", parts.Where(p => Math.Round(p.Value) != 0 && !(_solo && p.Ally)).Select(p => $"{Loc.T(p.LabelKey)} {N(p.Value)}"));

    /// A paragraph after the previous ones (nothing when empty).
    private static string Block(string text) => text == "" ? "" : "\n" + text;

    private static string Lines(Dictionary<string, double> map) =>
        string.Join('\n', map.Where(kv => Math.Round(kv.Value) != 0).OrderByDescending(kv => kv.Value).Select(kv => $"{GameNames.Of(kv.Key)}: {N(kv.Value)}"));

    private static string Powers(Dictionary<string, PowerTally> map) =>
        string.Join('\n', map.OrderByDescending(kv => kv.Value.Stacks).Select(kv => $"{GameNames.Of(kv.Key)}: x{kv.Value.Count} ({N(kv.Value.Stacks)})"));

    internal static Color CharacterColor(string? character) => character switch
    {
        "IRONCLAD" => new Color(0.85f, 0.3f, 0.25f),
        "SILENT" => new Color(0.35f, 0.75f, 0.4f),
        "DEFECT" => new Color(0.35f, 0.6f, 0.95f),
        "NECROBINDER" => new Color(0.7f, 0.45f, 0.9f),
        "REGENT" => new Color(0.95f, 0.65f, 0.25f),
        _ => new Color(0.75f, 0.75f, 0.75f)
    };

    /// The player's character color; a second player with the same character gets a lighter shade (same in the
    /// panel and in the charts).
    internal static Color PlayerColor(ulong id)
    {
        var players = Capture.Engine.Players;
        var character = players.GetValueOrDefault(id)?.Character ?? "";
        var before = players.Keys.Where(other => other < id).Count(other => players[other].Character == character);
        return CharacterColor(character).Lightened(0.3f * before);
    }

    internal static Label MakeLabel(string text, Color color, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeFontSizeOverride("font_size", FontSize);
        l.AddThemeConstantOverride("line_spacing", 0);
        return l;
    }

    internal static Button MakeButton(string text)
    {
        var b = new Button { Text = text, Flat = true, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", FontSize);
        b.AddThemeColorOverride("font_color", Text);
        // Tighter buttons: the game's theme pads them generously.
        var tight = new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 0, ContentMarginBottom = 0 };
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" }) b.AddThemeStyleboxOverride(state, tight);
        return b;
    }
}
