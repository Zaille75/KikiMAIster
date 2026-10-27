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
        _update = RecordStore.Add(run, names, victory);
        _showPending = true;
    }

    public static void Tick()
    {
        if (!_showPending || _panel == null) return;
        _showPending = false;
        Show(ended: true);
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
        Rebuild(ended);
        _panel.Visible = true;
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
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("RunSummary.Close", () => _panel!.Visible = false);
        header.AddChild(close);
        _content.AddChild(header);

        var grid = new GridContainer { Columns = 5, MouseFilter = Control.MouseFilterEnum.Pass };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 1);
        foreach (var h in new[] { "run.trophy", "run.winner", "run.value", "run.record", "run.personal" })
            grid.AddChild(Overlay.MakeLabel(Loc.T(h), Overlay.Muted));

        var book = RecordStore.Book;
        foreach (var c in Trophies.All.OrderBy(c => c.Fun))
        {
            if (c.CoopOnly && run.Count < 2) continue;
            var winner = Trophies.Winner(c, run);
            var name = Overlay.MakeLabel(Loc.T(c.Key), c.Fun ? Overlay.Muted : Overlay.Text);
            name.TooltipText = Loc.T($"{c.Key}.tip");
            name.MouseFilter = Control.MouseFilterEnum.Stop;
            grid.AddChild(name);
            if (winner is not { } w)
            {
                grid.AddChild(Overlay.MakeLabel("-", Overlay.Muted));
                grid.AddChild(Overlay.MakeLabel("", Overlay.Muted));
            }
            else
            {
                grid.AddChild(Overlay.MakeLabel(engine.Players.GetValueOrDefault(w.Id)?.Name ?? w.Id.ToString(CultureInfo.InvariantCulture),
                    Overlay.PlayerColor(w.Id)));
                grid.AddChild(Overlay.MakeLabel(Overlay.N(w.Value), Overlay.Text, HorizontalAlignment.Right));
            }
            var best = book.Best(c.Key);
            var newRecord = update?.NewRecords.Contains(c.Key) == true;
            grid.AddChild(Overlay.MakeLabel(best == null ? "-" : $"{(newRecord ? "★ " : "")}{Overlay.N(best.Value)} ({best.Name}, {best.Date})",
                newRecord ? Overlay.Accent : Overlay.Muted));
            var personal = winner is { } pw ? book.Personal(c.Key, pw.Id) : null;
            var newPersonal = winner is { } nw && update?.NewPersonal.Contains((c.Key, nw.Id)) == true;
            grid.AddChild(Overlay.MakeLabel(personal == null ? "-" : $"{(newPersonal ? "★ " : "")}{Overlay.N(personal.Value)}",
                newPersonal ? Overlay.Accent : Overlay.Muted));
        }
        _content.AddChild(grid);
        _content.AddChild(Overlay.MakeLabel(Loc.T(ended ? "run.recordsNote" : "run.inProgressNote"), Overlay.Muted));
        _panel!.CallDeferred(Control.MethodName.ResetSize);
    }
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
            });
            return _book;
        }
    }

    public static RecordBook.Update? Add(IReadOnlyDictionary<ulong, PlayerStats> run, IReadOnlyDictionary<ulong, string> names, bool victory)
    {
        RecordBook.Update? update = null;
        Capture.Safe("RecordStore.Save", () =>
        {
            update = Book.Add(run, names, victory, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (FilePath != null) File.WriteAllText(FilePath, Book.Write());
            ModLog.Info($"records updated: {update.NewRecords.Count} new records, {update.NewPersonal.Count} personal bests");
        });
        return update;
    }
}
