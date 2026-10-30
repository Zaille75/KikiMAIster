using System.Globalization;
using Godot;
using KikiMAIster.Core;

namespace KikiMAIster.UI;

/// End-of-run summary (spec 02-ui §3): the run's trophies (best player per category), with the all-time record and
/// each winner's personal best. Opens by itself when the run ends; the "Trophies" button of the combat summary opens
/// it at any time (the run so far, records unchanged until the run ends).
public static class RunSummary
{
    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static bool _showPending;
    private static bool _dragging;
    /// What the open window shows: the finished run (true) or the run in progress.
    private static bool _shownEnded;

    /// The finished run (its run-scope stats), its result and what it changed in the record book.
    private static IReadOnlyDictionary<ulong, PlayerStats>? _endedRun;
    private static bool _victory;
    private static RecordBook.Update? _update;

    public static void Attach(CanvasLayer layer)
    {
        _panel = new PanelContainer { Visible = false, MouseDefaultCursorShape = Control.CursorShape.Move };
        _panel.AddThemeStyleboxOverride("panel", Overlay.WindowStyle(0.94f));
        _panel.GuiInput += OnDrag;
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 4);
        _panel.AddChild(_content);
        layer.AddChild(_panel);
    }

    /// Victory or defeat: records are updated once per run, the window opens on the next timer tick.
    public static void OnRunEnded(bool victory)
    {
        var run = Capture.Engine.Get(Scope.Run);
        if (ReferenceEquals(run, _endedRun) || run.Count == 0) return;
        _endedRun = run;
        _victory = victory;
        var names = Capture.Engine.Players.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        var characters = Capture.Engine.Players.ToDictionary(kv => kv.Key, kv => kv.Value.Character);
        _update = RecordStore.Add(run, names, characters, victory, Patches.CombatFlowPatches.Ascension);
        CompositionStore.Add(characters, Patches.CombatFlowPatches.Ascension, victory);
        _showPending = true;
    }

    public static void Tick()
    {
        if (_panel == null) return;
        if (_showPending)
        {
            _showPending = false;
            Show(ended: true);
        }
        KeepClear();
    }

    /// Rebuilds the open window (language changed).
    public static void Refresh()
    {
        if (_panel is { Visible: true }) Rebuild(_shownEnded);
    }

    /// The window's rectangle on screen when it is open.
    internal static Rect2? PanelRect => _panel is { Visible: true } p && GodotObject.IsInstanceValid(p) ? new Rect2(p.Position, p.Size) : null;

    /// Keeps out of the way of the panel and of the summary window: moves next to them (right, left, below, above),
    /// wherever it fits on screen. Not saved.
    private static void KeepClear()
    {
        if (_panel is not { Visible: true } || _dragging) return;
        var others = new[] { Overlay.PanelRect, CombatSummary.PanelRect }.OfType<Rect2>().ToList();
        var size = _panel.Size;
        var rect = new Rect2(_panel.Position, size);
        if (!others.Any(o => o.Intersects(rect))) return;
        var screen = _panel.GetViewportRect().Size;
        var candidates = others.SelectMany(o => new[]
        {
            new Vector2(o.End.X + 10, rect.Position.Y), new Vector2(o.Position.X - 10 - size.X, rect.Position.Y),
            new Vector2(rect.Position.X, o.End.Y + 10), new Vector2(rect.Position.X, o.Position.Y - 10 - size.Y),
            new Vector2(o.End.X + 10, 0), new Vector2(o.Position.X - 10 - size.X, 0),
        }).Select(p => new Vector2(Math.Clamp(p.X, 0, Math.Max(0, screen.X - size.X)), Math.Clamp(p.Y, 0, Math.Max(0, screen.Y - size.Y))));
        foreach (var p in candidates)
        {
            if (others.Any(o => o.Intersects(new Rect2(p, size)))) continue;
            _panel.Position = p;
            return;
        }
    }

    public static void Hide()
    {
        if (_panel != null && _panel.Visible) _panel.Visible = false;
    }

    public static void Toggle()
    {
        if (_panel == null) return;
        if (_panel.Visible) _panel.Visible = false;
        else Show(ended: ReferenceEquals(Capture.Engine.Get(Scope.Run), _endedRun));
    }

    private static void Show(bool ended)
    {
        if (_panel == null || _content == null) return;
        _shownEnded = ended;
        Rebuild(ended);
        _panel.Visible = true;
        _panel.MoveToFront();
        _panel.CallDeferred(Control.MethodName.ResetSize);
        var screen = _panel.GetViewportRect().Size;
        _panel.Position = new Vector2(Math.Max(0, (screen.X - 560) / 2), 90);
    }

    private static void OnDrag(InputEvent e)
    {
        if (_panel == null) return;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb: _dragging = mb.Pressed; break;
            case InputEventMouseMotion mm when _dragging: _panel.Position += mm.Relative; break;
        }
    }

    /// Chifumi records (v0.17.45, their own foldable section): every pair of this run's players across all runs, what each
    /// one threw over all fights, and the longest fight. This run's figures are in the panel (Chifumi section).
    private static void AddChifumi(IReadOnlyDictionary<ulong, PlayerStats> run, StatsEngine engine)
    {
        string Name(ulong id) => engine.Players.GetValueOrDefault(id)?.Name ?? ChifumiStore.Book.NameOf(id);
        var open = ModConfig.Current.ChifumiRecordsOpen;
        var header = Overlay.MakeButton($"{(open ? "▾" : "▸")} {Loc.T("run.chifumi")}");
        header.Alignment = HorizontalAlignment.Left;
        header.AddThemeColorOverride("font_color", Overlay.Accent);
        header.Pressed += () => Capture.Safe("RunSummary.ChifumiFold", () =>
        {
            ModConfig.Current.ChifumiRecordsOpen = !ModConfig.Current.ChifumiRecordsOpen;
            ModConfig.Current.Save();
            Refresh();
        });
        _content!.AddChild(header);
        if (!open) return;
        var book = ChifumiStore.Book;
        var lines = new List<string>();
        var ids = run.Keys.OrderBy(id => id).ToList();
        for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
            {
                var (won, lost) = book.Against(ids[i], ids[j]);
                if (won + lost == 0) continue;
                var rate = ((double)won / (won + lost)).ToString("P0", CultureInfo.InvariantCulture);
                lines.Add(string.Format(Loc.T("run.chifumiPair"), Name(ids[i]), Name(ids[j]), won, lost, rate));
            }
        foreach (var id in ids)
        {
            var moves = book.MovesOf(id);
            if (moves.Count == 0) continue;
            lines.Add(string.Format(Loc.T("run.chifumiMoves"), Name(id), moves.GetValueOrDefault("Rock"), moves.GetValueOrDefault("Paper"),
                moves.GetValueOrDefault("Scissors")));
        }
        if (book.Longest.Rounds > 0)
            lines.Add(string.Format(Loc.T("run.chifumiLongest"), book.Longest.Rounds, Name(book.Longest.Winner), book.Longest.Date));
        if (lines.Count == 0) lines.Add(Loc.T("run.chifumiNone"));
        foreach (var line in lines) _content.AddChild(Overlay.MakeLabel("  " + line, Overlay.Text));
    }

    private static string NameOf(StatsEngine engine, ulong id) =>
        engine.Players.GetValueOrDefault(id)?.Name ?? id.ToString(CultureInfo.InvariantCulture);

    /// A grid of trophies with its column headers. Solo (v0.18.2): no winner nor personal best (always the one player),
    /// and the character record is headed with the character played ("Record Régent").
    private static GridContainer NewGrid(string? soloCharacter)
    {
        var solo = soloCharacter != null;
        var heads = solo ? new[] { "run.trophy", "run.value", "run.record", "run.character" }
            : new[] { "run.trophy", "run.winner", "run.value", "run.record", "run.personal", "run.character" };
        var grid = new GridContainer { Columns = heads.Length, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 1);
        foreach (var h in heads)
        {
            var text = solo && h == "run.character" && soloCharacter != "" ? string.Format(Loc.T("run.characterSolo"), GameNames.Of($"Character:{soloCharacter}")) : Loc.T(h);
            var l = Overlay.MakeLabel(text, Overlay.Muted);
            if (Loc.Has($"{h}.tip")) HoverTip.Set(l, Loc.T($"{h}.tip"));
            grid.AddChild(l);
        }
        return grid;
    }

    /// One trophy: its winners (every tied player, v0.17.45), the value, the records.
    private static void AddTrophy(GridContainer grid, Trophies.Category c, IReadOnlyDictionary<ulong, PlayerStats> run, StatsEngine engine,
        RecordBook.Update? update, bool ended)
    {
        var level = Patches.CombatFlowPatches.Ascension;
        var book = RecordStore.Book;
        var coop = run.Count > 1;
        var solo = !coop;
        var winners = Trophies.Winners(c, run, victory: !ended || _victory);
        var name = Overlay.MakeLabel(Loc.T(c.Key), c.Fun ? Overlay.Muted : Overlay.Text);
        HoverTip.Set(name, Loc.T($"{c.Key}.tip"));
        grid.AddChild(name);
        (ulong Id, double Value)? winner = winners.Count > 0 ? winners[0] : null;
        if (winner is not { } w)
        {
            if (!solo) grid.AddChild(Overlay.MakeLabel("-", Overlay.Muted));
            grid.AddChild(Overlay.MakeLabel(solo ? "-" : "", Overlay.Muted));
        }
        else
        {
            var names = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            names.AddThemeConstantOverride("separation", 0);
            for (var i = 0; i < winners.Count; i++)
                names.AddChild(Overlay.MakeLabel((i > 0 ? ", " : "") + NameOf(engine, winners[i].Id), Overlay.PlayerColor(winners[i].Id)));
            if (!solo) grid.AddChild(names);
            if (c.Participation) grid.AddChild(Overlay.MakeLabel("", Overlay.Muted));
            else
            {
                // Hovering the winning value shows every player's score (v0.17.0).
                var value = Overlay.MakeLabel(Overlay.N(w.Value), Overlay.Text, HorizontalAlignment.Right);
                HoverTip.Set(value, Loc.T("run.others") + ":\n" + string.Join('\n', run.Where(kv => c.CanWin(kv.Value))
                    .Select(kv => (Id: kv.Key, Value: c.Value(kv.Value)))
                    .OrderBy(x => c.LowerIsBetter ? x.Value : -x.Value).ThenBy(x => x.Id)
                    .Select(x => $"{NameOf(engine, x.Id)}: {Overlay.N(x.Value)}")));
                grid.AddChild(value);
            }
        }
        if (c.Participation)
        {
            for (var i = 0; i < (solo ? 2 : 3); i++) grid.AddChild(Overlay.MakeLabel("", Overlay.Muted));
            return;
        }
        // Records at this run's ascension level (v0.17.24); hovering shows the record of every level together.
        var best = book.Best(c.Key, coop, level);
        var newRecord = update?.NewRecords.Contains(c.Key) == true;
        // Solo (v0.18.3): no name on the line (the holder, a character, is in the tooltip).
        var recordCell = Overlay.MakeLabel(best == null ? "-" : $"{(newRecord ? "★ " : "")}{Overlay.N(best.Value)} ({(solo ? "" : best.Name + ", ")}{best.Date})",
            newRecord ? Overlay.Accent : Overlay.Muted);
        var tips = new List<string>();
        if (solo && best != null) tips.Add(best.Name);
        if (book.Best(c.Key, coop) is { } anyLevel)
            tips.Add($"{Loc.T("run.allLevels")}: {Overlay.N(anyLevel.Value)} ({anyLevel.Name}, {anyLevel.Date})");
        if (tips.Count > 0) HoverTip.Set(recordCell, string.Join('\n', tips));
        grid.AddChild(recordCell);
        var personal = winner is { } pw ? book.Personal(c.Key, pw.Id, coop, level) : null;
        var newPersonal = winner is { } nw && update?.NewPersonal.Contains((c.Key, nw.Id)) == true;
        if (!solo)
            grid.AddChild(Overlay.MakeLabel(personal == null ? "-" : $"{(newPersonal ? "★ " : "")}{Overlay.N(personal.Value)}",
                newPersonal ? Overlay.Accent : Overlay.Muted));
        // The best of the winner's character (in solo: the character played); the tooltip lists every character.
        var character = winner is { } cw ? engine.Players.GetValueOrDefault(cw.Id)?.Character ?? "" : "";
        var ofCharacter = character == "" ? null : book.OfCharacter(c.Key, character, coop, level);
        var newCharacter = update?.NewCharacter.Contains((c.Key, character)) == true;
        // Solo: the header names the character, so the cell keeps only the date.
        var cell = Overlay.MakeLabel(ofCharacter == null ? "-" : $"{(newCharacter ? "★ " : "")}{Overlay.N(ofCharacter.Value)} ({(solo ? "" : ofCharacter.Name + ", ")}{ofCharacter.Date})",
            newCharacter ? Overlay.Accent : Overlay.Muted);
        var all = book.Characters(c.Key, coop, level).ToList();
        if (all.Count > 0)
            HoverTip.Set(cell, string.Join('\n', all.Select(x =>
                $"{GameNames.Of($"Character:{x.Character}")}: {Overlay.N(x.Entry.Value)} ({x.Entry.Name}, {x.Entry.Date})")));
        grid.AddChild(cell);
    }

    private static void Rebuild(bool ended)
    {
        foreach (var child in _content!.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }
        var engine = Capture.Engine;
        var run = ended && _endedRun != null ? _endedRun : engine.Get(Scope.Run);
        var update = ended ? _update : null;

        var header = new HBoxContainer();
        var title = Overlay.MakeLabel(Loc.T("run.title") + " · " +
            (ended ? Loc.T(_victory ? "summary.victory" : "summary.defeat") : Loc.T("summary.inProgress")), Overlay.Accent);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        // The fun trophies have their own section, shown or not (v0.17.45).
        var fun = Overlay.MakeButton(Loc.T(ModConfig.Current.FunTrophiesShown ? "run.funHide" : "run.funShow"));
        fun.Pressed += () => Capture.Safe("RunSummary.Fun", () =>
        {
            ModConfig.Current.FunTrophiesShown = !ModConfig.Current.FunTrophiesShown;
            ModConfig.Current.Save();
            Refresh();
        });
        header.AddChild(fun);
        var compos = Overlay.MakeButton(Loc.T("compo.button"));
        compos.TooltipText = Loc.T("compo.buttonTip");
        compos.Pressed += () => Capture.Safe("RunSummary.Compositions", CompositionWindow.Toggle);
        header.AddChild(compos);
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("RunSummary.Close", () => _panel!.Visible = false);
        header.AddChild(close);
        _content.AddChild(header);

        var level = Patches.CombatFlowPatches.Ascension;
        var coop = run.Count > 1;
        bool Shown(Trophies.Category c) => !(c.CoopOnly && run.Count < 2) && !(ended && c.NeedsVictory && !_victory);
        string? soloCharacter = coop ? null : run.Keys.Select(id => engine.Players.GetValueOrDefault(id)?.Character).FirstOrDefault() ?? "";
        var grid = NewGrid(soloCharacter);
        foreach (var c in Trophies.All.Where(c => !c.Fun && Shown(c))) AddTrophy(grid, c, run, engine, update, ended);
        _content.AddChild(grid);
        if (ModConfig.Current.FunTrophiesShown)
        {
            _content.AddChild(Overlay.MakeLabel(Loc.T("run.funTitle"), Overlay.Accent));
            var funGrid = NewGrid(soloCharacter);
            foreach (var c in Trophies.All.Where(c => c.Fun && Shown(c))) AddTrophy(funGrid, c, run, engine, update, ended);
            _content.AddChild(funGrid);
        }
        if (coop) AddChifumi(run, engine);
        _content.AddChild(Overlay.MakeLabel(Loc.T(coop ? "run.coopRecords" : "run.soloRecords") + $" A{level.ToString(CultureInfo.InvariantCulture)} · " +
            Loc.T(ended ? "run.recordsNote" : "run.inProgressNote"), Overlay.Muted));
        _panel!.CallDeferred(Control.MethodName.ResetSize);
    }
}

/// Rock-paper-scissors results across runs, saved next to the DLL (spec 01-metrics §C4).
public static class ChifumiStore
{
    private static Chifumi? _book;

    private static string? FilePath
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(ChifumiStore).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "KikiMAIster.chifumi.cfg");
        }
    }

    public static Chifumi Book
    {
        get
        {
            if (_book != null) return _book;
            _book = new Chifumi();
            Capture.Safe("ChifumiStore.Load", () =>
            {
                if (FilePath != null && File.Exists(FilePath)) _book = Chifumi.Read(File.ReadAllText(FilePath));
            });
            return _book;
        }
    }

    public static void Record(string fightId, ulong winner, IEnumerable<ulong> players, IReadOnlyDictionary<ulong, string> names,
        IReadOnlyDictionary<ulong, IReadOnlyList<string>>? moves = null, int rounds = 0) =>
        Capture.Safe("ChifumiStore.Save", () =>
        {
            if (Book.Record(fightId, winner, players, names, moves, rounds, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) &&
                FilePath != null) File.WriteAllText(FilePath, Book.Write());
        });
}

/// The record book, saved next to the DLL (not a .json file: the game reads every .json in mods/ as a manifest).
public static class RecordStore
{
    private static RecordBook? _book;

    private static string? FilePath
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(RecordStore).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "KikiMAIster.records.cfg");
        }
    }

    public static RecordBook Book
    {
        get
        {
            if (_book != null) return _book;
            _book = new RecordBook();
            Capture.Safe("RecordStore.Load", () =>
            {
                if (FilePath != null && File.Exists(FilePath)) _book = RecordBook.Read(File.ReadAllText(FilePath));
                // Once (v0.17.1): "Maître des malus" counted À l'attaque !, which is no debuff: its records start over.
                if (ModConfig.Current.DebuffRecordsCleared) return;
                _book.Clear("trophy.debuffs");
                if (FilePath != null && File.Exists(FilePath)) File.WriteAllText(FilePath, _book.Write());
                ModConfig.Current.DebuffRecordsCleared = true;
                ModConfig.Current.Save();
                ModLog.Info("Maître des malus records cleared (À l'attaque ! is no longer a debuff)");
            });
            return _book;
        }
    }

    public static RecordBook.Update? Add(IReadOnlyDictionary<ulong, PlayerStats> run, IReadOnlyDictionary<ulong, string> names,
        IReadOnlyDictionary<ulong, string> characters, bool victory, int ascension)
    {
        RecordBook.Update? update = null;
        Capture.Safe("RecordStore.Save", () =>
        {
            update = Book.Add(run, names, victory, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), characters, ascension);
            if (FilePath != null) File.WriteAllText(FilePath, Book.Write());
            ModLog.Info($"records updated: {update.NewRecords.Count} new records, {update.NewPersonal.Count} personal bests");
        });
        return update;
    }
}
