using System.Globalization;
using Godot;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

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
    /// The character-select screen while it is open (set by UiPatches).
    public static WeakReference<NCharacterSelectScreen>? Lobby;

    /// The character-select screen is open (outside a run).
    internal static bool InLobby => Lobby != null && Lobby.TryGetTarget(out var screen) && GodotObject.IsInstanceValid(screen) &&
                                    screen.IsInsideTree() && screen.IsVisibleInTree();
    private static string _contextSignature = "";

    /// Opened while the lobby was open: closes by itself once the lobby is gone (v0.17.0).
    private static bool _openedInLobby;

    /// Where the player is: Solo true/false (null = unknown: main menu…), players, ascension, and the group's
    /// characters ("" when not all chosen). From the run being played, else from the character-select lobby.
    /// Ids / Names: the players present (co-op), for the "this group" stats.
    private sealed record Context(bool? Solo, int Players, int? Ascension, string Group, IReadOnlyList<ulong> Ids, IReadOnlyList<string> Names);

    private static Context Current()
    {
        if (Overlay.InRun)
        {
            var count = Patches.CombatFlowPatches.PlayerCount;
            var players = Capture.Engine.Players.Values.ToList();
            var group = players.Count == count ? Compositions.KeyOf(players.Select(p => p.Character)) : "";
            var ids = Capture.Engine.Players.Keys.ToList();
            return new Context(count == 1, count, Patches.CombatFlowPatches.Ascension, group, ids,
                Capture.Engine.Players.Values.Select(p => p.Name).ToList());
        }
        if (Lobby != null && Lobby.TryGetTarget(out var screen) && GodotObject.IsInstanceValid(screen) && screen.Lobby is { } lobby)
        {
            var solo = lobby.NetService.Type == NetGameType.Singleplayer;
            var known = Characters();
            var chars = lobby.Players.Select(p => p.character?.Id.Entry ?? "").ToList();
            var group = chars.Count > 0 && chars.All(known.Contains) ? Compositions.KeyOf(chars) : "";
            var ids = lobby.Players.Select(p => p.id).ToList();
            return new Context(solo, solo ? 1 : Math.Max(2, lobby.Players.Count), lobby.Ascension, group, ids,
                ids.Select(id => LobbyName(lobby.NetService, id)).ToList());
        }
        return new Context(null, 0, null, "", Array.Empty<ulong>(), Array.Empty<string>());
    }

    private static string LobbyName(INetGameService net, ulong id)
    {
        try
        {
            var name = MegaCrit.Sts2.Core.Platform.PlatformUtil.GetPlayerNameRaw(net.Platform, id);
            return string.IsNullOrWhiteSpace(name) ? id.ToString(CultureInfo.InvariantCulture) : name;
        }
        catch (Exception)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// "This group" can be shown: a co-op group whose players are known.
    private static bool HasGroup(Context c) => c.Solo == false && c.Ids.Count > 1;

    /// The tallies shown: every run of this profile, or only the runs of exactly this group of players.
    private static Compositions DataFor(Context c) =>
        ModConfig.Current.CompoGroupOnly && HasGroup(c) ? CompositionStore.OfGroup(c.Ids) : CompositionStore.Data;

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
            if (_panel is not { Visible: true }) return;
            // Opened in the lobby: gone with it (the run started or the player left).
            if (_openedInLobby && !InLobby)
            {
                _openedInLobby = false;
                _panel.Visible = false;
                return;
            }
            // The lobby changed (a player joined, a character or the ascension was chosen): follow it.
            var context = Current();
            var signature = Signature(context);
            if (signature != _contextSignature)
            {
                _contextSignature = signature;
                if (context.Players > 0) _players = context.Players;
                if (context.Ascension != null) _ascension = context.Ascension;
                _rebuildPending = true;
            }
            if (!_rebuildPending) return;
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
        var context = Current();
        _contextSignature = Signature(context);
        _openedInLobby = InLobby && !Overlay.InRun;
        _players = context.Players > 0 ? context.Players : _players > 0 ? _players : 2;
        _ascension = context.Ascension ?? CompositionStore.Data.MostPlayedAscension();
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

    private static string Signature(Context c) => $"{c.Solo}|{c.Players}|{c.Ascension}|{c.Group}|{string.Join(',', c.Ids)}";

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
        var context = Current();
        var cfg = ModConfig.Current;
        var data = DataFor(context);
        var characters = Characters();

        var header = new HBoxContainer();
        var title = Overlay.MakeLabel(Loc.T("compo.title"), Overlay.Accent);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("CompositionWindow.Close", () => _panel!.Visible = false);
        header.AddChild(close);
        _content.AddChild(header);

        // Whose stats: every run of this profile, or only this group of players (co-op, v0.17.0).
        if (HasGroup(context))
        {
            var whose = new HBoxContainer();
            whose.AddChild(Overlay.MakeLabel(Loc.T("compo.stats.of"), Overlay.Text));
            foreach (var groupOnly in new[] { false, true })
            {
                var b = Overlay.MakeButton(Loc.T(groupOnly ? "compo.thisGroup" : "compo.mine"));
                b.AddThemeColorOverride("font_color", cfg.CompoGroupOnly == groupOnly ? Overlay.Accent : Overlay.Muted);
                if (groupOnly) b.TooltipText = string.Format(Loc.T("compo.thisGroupTip"), string.Join(", ", context.Names));
                b.Pressed += () => Capture.Safe("CompositionWindow.Whose", () =>
                {
                    cfg.CompoGroupOnly = groupOnly;
                    cfg.Save();
                    Pick();
                    _rebuildPending = true;
                });
                whose.AddChild(b);
            }
            _content.AddChild(whose);
            if (cfg.CompoGroupOnly && !CompositionStore.HistoryRead)
                _content.AddChild(Overlay.MakeLabel(Loc.T("compo.noHistory"), Overlay.Muted));
        }

        var count = new HBoxContainer();
        count.AddChild(Overlay.MakeLabel(Loc.T("compo.players"), Overlay.Text));
        foreach (var n in new[] { 1, 2, 3, 4 })
        {
            var b = Overlay.MakeButton(n.ToString(CultureInfo.InvariantCulture));
            b.AddThemeColorOverride("font_color", n == _players ? Overlay.Accent : Overlay.Muted);
            // Solo: only 1 player; co-op: 2 or more (greyed out otherwise).
            b.Disabled = context.Solo == true ? n != 1 : context.Solo == false && n == 1;
            b.AddThemeColorOverride("font_disabled_color", new Color(Overlay.Muted, 0.35f));
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

        // The group being played or being chosen in the lobby, at its ascension level.
        if (context.Group != "")
            _content.AddChild(Overlay.MakeLabel($"{Loc.T("compo.group")} {Describe(context.Group)} · " +
                (context.Ascension is { } a ? string.Format(Loc.T("compo.at"), a) : "") + Stats(data.Get(context.Group, context.Ascension)),
                Overlay.Text));

        // Sort: by games or by win rate, decreasing or increasing (v0.17.0).
        var sort = new HBoxContainer();
        sort.AddChild(Overlay.MakeLabel(Loc.T("compo.order"), Overlay.Text));
        foreach (var (order, label) in new[] { ("games", "compo.byGames"), ("winrate", "compo.byWinRate") })
        {
            var b = Overlay.MakeButton(Loc.T(label));
            b.AddThemeColorOverride("font_color", cfg.CompoOrder == order ? Overlay.Accent : Overlay.Muted);
            b.Pressed += () => Capture.Safe("CompositionWindow.Order", () =>
            {
                cfg.CompoOrder = order;
                cfg.Save();
                _rebuildPending = true;
            });
            sort.AddChild(b);
        }
        var direction = Overlay.MakeButton(Loc.T(cfg.CompoDescending ? "compo.descending" : "compo.ascending"));
        direction.AddThemeColorOverride("font_color", Overlay.Accent);
        direction.TooltipText = Loc.T("compo.directionTip");
        direction.Pressed += () => Capture.Safe("CompositionWindow.Direction", () =>
        {
            cfg.CompoDescending = !cfg.CompoDescending;
            cfg.Save();
            _rebuildPending = true;
        });
        sort.AddChild(direction);
        _content.AddChild(sort);

        // Every played composition (v0.17.0: no longer cut at 12), scrolled when the list is long.
        var compositions = data.Played(_players, _ascension, cfg.CompoOrder == "winrate" ? Compositions.Order.WinRate : Compositions.Order.Games,
            cfg.CompoDescending).ToList();
        if (compositions.Count == 0) _content.AddChild(Overlay.MakeLabel(Loc.T("compo.none"), Overlay.Muted));
        else
        {
            var grid = new GridContainer { Columns = 2, MouseFilter = Control.MouseFilterEnum.Pass };
            grid.AddThemeConstantOverride("h_separation", 14);
            grid.AddThemeConstantOverride("v_separation", 0);
            foreach (var (key, tally) in compositions)
            {
                grid.AddChild(Overlay.MakeLabel(Describe(key), Overlay.Text));
                grid.AddChild(Overlay.MakeLabel(Stats(tally), tally.Won > 0 ? Overlay.Text : Overlay.Muted));
            }
            const int maxRows = 18;
            if (compositions.Count <= maxRows) _content.AddChild(grid);
            else
            {
                var scroll = new ScrollContainer
                {
                    HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = Control.MouseFilterEnum.Pass,
                    CustomMinimumSize = new Vector2(0, maxRows * 18)
                };
                scroll.AddChild(grid);
                _content.AddChild(scroll);
            }
        }

        // Suggestions: a composition never played, one never won.
        AddSuggestion("compo.neverPlayed", data.NeverPlayed(characters, _players, _ascension).ToList(), _neverPlayedPick, () => _neverPlayedPick++);
        // Never won = played and always lost: only shown once such a composition exists.
        var neverWon = data.NeverWon(characters, _players, _ascension).ToList();
        if (neverWon.Count > 0) AddSuggestion("compo.neverWon", neverWon, _neverWonPick, () => _neverWonPick++);
    }

    private static void AddSuggestion(string labelKey, List<string> options, int pick, Action next)
    {
        var row = new HBoxContainer();
        row.AddChild(Overlay.MakeLabel(string.Format(Loc.T(labelKey), options.Count), Overlay.Accent));
        row.AddChild(Overlay.MakeLabel(options.Count == 0 ? "-" : Describe(options[pick % options.Count]), Overlay.Text));
        if (options.Count > 1)
        {
            // A "turning arrows" button (v0.17.0), clearer than the "Another one" text, which stays as its tooltip.
            var another = Overlay.MakeButton("↻");
            another.TooltipText = Loc.T("compo.another");
            another.AddThemeColorOverride("font_color", Overlay.Accent);
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
    /// The finished runs of the game's history (and of this session), with their players: "this group" stats.
    private static readonly List<Compositions.Run> Runs = new();

    /// The game's run history could be read (else "this group" has no data).
    public static bool HistoryRead { get; private set; }

    /// The tallies of the runs played by exactly these players.
    public static Compositions OfGroup(IReadOnlyCollection<ulong> players)
    {
        _ = Data;
        return Compositions.OfGroup(Runs, players);
    }

    private static string? FilePath
    {
        get
        {
            var dir = Path.GetDirectoryName(typeof(CompositionStore).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "KikiMAIster.compositions.cfg");
        }
    }

    /// Rebuilt from the game's run history once per game session (the first time it is needed), then kept up to date
    /// by the runs that end during the session. The saved file is only a fallback when the history cannot be read.
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
            Capture.Safe("CompositionStore.History", () =>
            {
                if (FromHistory() is not { } history) return;
                _data = Compositions.Of(history);
                Runs.Clear();
                Runs.AddRange(history);
                HistoryRead = true;
                if (FilePath != null) File.WriteAllText(FilePath, _data.Write());
            });
            return _data;
        }
    }

    /// Every finished standard run of the game's history (this profile): its players' characters, ascension and result.
    /// Daily and custom runs, and abandoned runs, are left out. Null when the history cannot be read.
    private static List<Compositions.Run>? FromHistory()
    {
        var save = SaveManager.Instance;
        if (save == null) return null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = new List<Compositions.Run>();
        int read = 0, counted = 0, failed = 0;
        foreach (var name in save.GetAllRunHistoryNames())
        {
            read++;
            try
            {
                var run = save.LoadRunHistory(name);
                if (!run.Success || run.SaveData is not { } h)
                {
                    failed++;
                    continue;
                }
                if (h.GameMode != GameMode.Standard || h.WasAbandoned || h.Players.Count == 0) continue;
                result.Add(new Compositions.Run(h.Players.Select(p => p.Id).ToList(), h.Players.Select(p => p.Character.Entry).ToList(), h.Ascension, h.Win));
                counted++;
            }
            catch (Exception e)
            {
                failed++;
                ModLog.Error($"run history {name}", e);
            }
        }
        ModLog.Info($"compositions from the run history: {counted} runs counted out of {read} ({failed} unreadable) in {watch.ElapsedMilliseconds} ms");
        return read > 0 && failed < read ? result : null;
    }

    /// A finished run (victory or defeat) counts one game for its composition.
    public static void Add(IReadOnlyDictionary<ulong, string> players, int ascension, bool victory) => Capture.Safe("CompositionStore.Save", () =>
    {
        var list = players.Values.ToList();
        Data.Add(list, ascension, victory);
        Runs.Add(new Compositions.Run(players.Keys.ToList(), list, ascension, victory));
        if (FilePath != null) File.WriteAllText(FilePath, Data.Write());
        ModLog.Info($"composition A{ascension} {Compositions.KeyOf(list)}: {(victory ? "victory" : "defeat")} recorded");
    });
}
