using System.Globalization;
using System.IO.Compression;
using System.Text;
using Godot;
using KikiMAIster.Core;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace KikiMAIster.UI;

/// F9 or the settings window's button (v0.18.0: every build) opens a small window to write a bug report or an idea in
/// game. Each report is a folder in "feedback/" in the data folder, plus a zip of it: the message, a screenshot taken when F9
/// was pressed, the game state (hands, piles, enemies, intents, powers, relics, potions), the run's stats, the end of the
/// mod's log and the internal errors noted this session. Every text is anonymised first (spec 03-technical, "Reports").
/// Public builds then open the folder and the GitHub issue form; development builds only save.
public static class FeedbackWindow
{
    private const string IssueForm = "https://github.com/Zaille75/KikiMAIster/issues/new?template=report.yml";
    private const int MaxMessageInLink = 1500;

    /// Players' names and Steam ids, account and computer names: removed from every report's text.
    internal static readonly Anonymizer Privacy = new();

    private static PanelContainer? _panel;
    private static Label? _title;
    private static Label? _kindLabel;
    private static TextEdit? _text;
    private static Label? _errors;
    private static Label? _status;
    private static Button? _save;
    private static CheckBox? _withScreenshot;
    private static string _category = "bug";
    private static readonly Dictionary<string, Button> CategoryButtons = new();
    private static Image? _screenshot;
    private static string _state = "";
    private static bool _dragging;

    private static string? Folder
    {
        get
        {
            return DataDir.File("feedback");
        }
    }

    private static string Version => typeof(FeedbackWindow).Assembly.GetName().Version?.ToString(3) ?? "?";

    private static string Branch => GameBranch.Name;

    public static void Attach(CanvasLayer layer)
    {
        Capture.Safe("Feedback.Machine", () => Privacy.RegisterMachine(System.Environment.UserName, System.Environment.MachineName));
        _panel = new PanelContainer { Visible = false, MouseDefaultCursorShape = Control.CursorShape.Move };
        _panel.AddThemeStyleboxOverride("panel", Overlay.WindowStyle(0.96f));
        _panel.GuiInput += e => Capture.Safe("Feedback.Drag", () =>
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb: _dragging = mb.Pressed; break;
                case InputEventMouseMotion mm when _dragging: _panel.Position += mm.Relative; break;
            }
        });
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        var header = new HBoxContainer();
        _title = Overlay.MakeLabel("", Overlay.Accent);
        _title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(_title);
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("Feedback.Close", Hide);
        header.AddChild(close);
        box.AddChild(header);

        var kinds = new HBoxContainer();
        _kindLabel = Overlay.MakeLabel("", Overlay.Text);
        kinds.AddChild(_kindLabel);
        foreach (var key in new[] { "bug", "stat", "idee" })
        {
            var b = Overlay.MakeButton("");
            b.Pressed += () => Capture.Safe("Feedback.Kind", () =>
            {
                _category = key;
                Refresh();
            });
            CategoryButtons[key] = b;
            kinds.AddChild(b);
        }
        box.AddChild(kinds);

        _text = new TextEdit { CustomMinimumSize = new Vector2(460, 150), WrapMode = TextEdit.LineWrappingMode.Boundary };
        _text.AddThemeFontSizeOverride("font_size", Overlay.FontSize);
        box.AddChild(_text);

        _withScreenshot = new CheckBox { ButtonPressed = false, FocusMode = Control.FocusModeEnum.None };
        _withScreenshot.AddThemeFontSizeOverride("font_size", Overlay.FontSize);
        Overlay.StyleCheckBox(_withScreenshot);
        box.AddChild(_withScreenshot);

        _errors = Overlay.MakeLabel("", Overlay.Muted);
        box.AddChild(_errors);

        var actions = new HBoxContainer();
        _status = Overlay.MakeLabel("", Overlay.Muted);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(300, 0);
        actions.AddChild(_status);
        _save = Overlay.MakeButton("");
        _save.AddThemeColorOverride("font_color", Overlay.Accent);
        _save.Pressed += () => Capture.Safe("Feedback.Save", Save);
        actions.AddChild(_save);
        box.AddChild(actions);

        _panel.AddChild(box);
        layer.AddChild(_panel);
        Refresh();
    }

    /// F9: takes the screenshot and the game state at once (before the window covers the screen), then opens the window.
    public static void Toggle()
    {
        if (_panel == null) return;
        if (_panel.Visible)
        {
            Hide();
            return;
        }
        // Opened from the settings window: hide it first, so the screenshot shows the game.
        SettingsMenu.Hide();
        Capture.Safe("Feedback.Screenshot", () => _screenshot = _panel.GetViewport().GetTexture().GetImage());
        _state = "";
        Capture.Safe("Feedback.State", () => _state = GameState());
        _status!.Text = "";
        Refresh();
        _panel.Visible = true;
        _panel.MoveToFront();
        var screen = _panel.GetViewportRect().Size;
        _panel.Position = new Vector2(Math.Max(0, (screen.X - 480) / 2), Math.Max(0, screen.Y / 3 - 100));
        _text!.GrabFocus();
    }

    private static void Hide()
    {
        if (_panel != null) _panel.Visible = false;
        _text?.ReleaseFocus();
    }

    /// Texts in the current language, and the chosen type highlighted.
    private static void Refresh()
    {
        if (_title == null) return;
        _title.Text = Loc.T("report.title");
        _kindLabel!.Text = Loc.T("report.kind");
        foreach (var (key, b) in CategoryButtons)
        {
            b.Text = Loc.T($"report.kind.{key}");
            b.AddThemeColorOverride("font_color", key == _category ? Overlay.Accent : Overlay.Muted);
        }
        _text!.PlaceholderText = Loc.T("report.placeholder");
        _withScreenshot!.Text = Loc.T("report.screenshot");
        var errors = ModLog.NotedErrorCount;
        _errors!.Text = errors > 0 ? string.Format(Loc.T("report.errors"), errors) : "";
        _errors.Visible = errors > 0;
#if DEV_TOOLS
        _save!.Text = Loc.T("report.save");
#else
        _save!.Text = Loc.T("report.create");
#endif
    }

    private static void Save()
    {
        if (Folder == null || _text == null) return;
        var now = DateTime.Now;
        var name = $"{now:yyyy-MM-dd_HH-mm-ss}_{_category}";
        var dir = Path.Combine(Folder, name);
        Directory.CreateDirectory(dir);
        var message = _text.Text;
        // Players of earlier sessions kept in the chifumi records can appear in the log too.
        Capture.Safe("Feedback.KnownNames", () =>
        {
            foreach (var (id, known) in ChifumiStore.Book.Names) Privacy.Register(id, known);
        });
        Write(dir, "message.txt", $"Type: {_category}\nDate: {now:yyyy-MM-dd HH:mm:ss}\nMod: v{Version} ({Branch})\n\n{message}\n");
        Write(dir, "state.txt", _state);
        Capture.Safe("Feedback.Run", () => Write(dir, "run.txt", RunSnapshot.Write("feedback", 0, Capture.Engine)));
        Capture.Safe("Feedback.Log", () =>
        {
            var log = DataDir.File("KikiMAIster.log");
            if (log == null || !File.Exists(log)) return;
            using var stream = new FileStream(log, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            Write(dir, "log-tail.txt", string.Join('\n', lines.TakeLast(600)));
        });
        Write(dir, "errors.txt", ModLog.ErrorsText());
        if (_withScreenshot!.ButtonPressed && _screenshot != null)
            Capture.Safe("Feedback.Png", () => _screenshot.SavePng(Path.Combine(dir, "screenshot.png")));
        var zip = Path.Combine(Folder, $"KikiMAIster-report-{name}.zip");
        Capture.Safe("Feedback.Zip", () =>
        {
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(dir, zip);
        });
        ModLog.Info($"feedback saved: {name}");
        ModLog.ErrorsReported();
        _text.Text = "";
#if DEV_TOOLS
        _status!.Text = string.Format(Loc.T("report.saved"), Directory.GetDirectories(Folder).Length);
        Hide();
#else
        _status!.Text = Loc.T("report.next");
        Capture.Safe("Feedback.Folder", () => OS.ShellShowInFileManager(File.Exists(zip) ? zip : dir));
        Capture.Safe("Feedback.Issue", () => OS.ShellOpen(IssueLink(message)));
#endif
    }

    /// Anonymised before it touches the disk.
    private static void Write(string dir, string file, string text) =>
        File.WriteAllText(Path.Combine(dir, file), Privacy.Scrub(text));

    /// The GitHub issue form, filled with the type, version, branch and the (anonymised, shortened) message.
    private static string IssueLink(string message)
    {
        message = Privacy.Scrub(message.Trim());
        if (message.Length > MaxMessageInLink) message = message[..MaxMessageInLink] + "…";
        var kind = Loc.T($"report.kind.{_category}");
        var firstLine = message.Split('\n')[0];
        var title = $"[{kind}] {(firstLine.Length > 60 ? firstLine[..60] + "…" : firstLine)}";
        string Q(string s) => Uri.EscapeDataString(s);
        return $"{IssueForm}&title={Q(title)}&kind={Q(kind)}&version={Q(Version)}&branch={Q(Branch)}&description={Q(message)}";
    }

    /// What is on screen, in text: the run, every player (HP, block, energy, stars, powers, hand, piles, relics,
    /// potions) and every enemy (HP, block, powers, intent).
    private static string GameState()
    {
        var sb = new StringBuilder();
        var run = Patches.CombatFlowPatches.CurrentRun;
        var engine = Capture.Engine;
        sb.AppendLine($"In run: {Overlay.InRun} · floor {run?.TotalFloor} · ascension {run?.AscensionLevel} · combat {engine.CurrentCombat?.Number} " +
                      $"\"{engine.CurrentCombat?.Name}\" round {engine.Round}");
        if (run == null) return sb.ToString();
        foreach (var p in run.Players)
        {
            var c = p.Creature;
            sb.AppendLine();
            sb.AppendLine($"== {Capture.PlayerName(p, run.Players.Count)} ({p.Character.Id.Entry}, id {p.NetId}): HP {c.CurrentHp}/{c.MaxHp}, block {c.Block}");
            sb.AppendLine($"Powers: {Powers(c)}");
            if (p.PlayerCombatState is { } st)
            {
                sb.AppendLine($"Energy {st.Energy}, stars {st.Stars}");
                sb.AppendLine($"Hand: {Cards(st.Hand.Cards)}");
                sb.AppendLine($"Draw ({st.DrawPile.Cards.Count}): {Cards(st.DrawPile.Cards)}");
                sb.AppendLine($"Discard ({st.DiscardPile.Cards.Count}): {Cards(st.DiscardPile.Cards)}");
                sb.AppendLine($"Exhaust ({st.ExhaustPile.Cards.Count}): {Cards(st.ExhaustPile.Cards)}");
            }
            if (p.IsOstyAlive && p.Osty is { } pet) sb.AppendLine($"Pet: HP {pet.CurrentHp}/{pet.MaxHp}, powers {Powers(pet)}");
            sb.AppendLine($"Relics: {string.Join(", ", p.Relics.Select(r => Name("Relic", r)))}");
            sb.AppendLine($"Potions: {string.Join(", ", p.Potions.Select(x => Name("Potion", x)))}");
            sb.AppendLine($"Deck ({p.Deck.Cards.Count()}): {Cards(p.Deck.Cards)}");
        }
        var combat = run.Players.FirstOrDefault()?.Creature.CombatState;
        if (combat != null)
        {
            sb.AppendLine();
            sb.AppendLine("== Enemies");
            foreach (var e in combat.Enemies)
            {
                var intent = "";
                Capture.Safe("Feedback.Intent", () =>
                {
                    if (e.Monster?.NextMove is { } move)
                        intent = $"{move.Id}: " + string.Join(", ", move.Intents.Select(i => i is AttackIntent a
                            ? $"attack {a.GetTotalDamage(run.Players.Select(x => x.Creature).ToArray(), e)}" : i.GetType().Name));
                });
                sb.AppendLine($"{e.Name}: HP {e.CurrentHp}/{e.MaxHp}, block {e.Block}, alive {e.IsAlive}, powers {Powers(e)}, intent {intent}");
            }
        }
        return sb.ToString();
    }

    private static string Name(string kind, AbstractModel m) => $"{GameNames.Of($"{kind}:{Capture.IdOf(m)}")} ({m.Id.Entry})";

    private static string Cards(IEnumerable<CardModel> cards) =>
        string.Join(", ", cards.Select(c => $"{GameNames.Of($"Card:{Capture.IdOf(c)}")}{(c.IsUpgraded ? "+" : "")}"));

    private static string Powers(Creature c) =>
        c.Powers.Count == 0 ? "-" : string.Join(", ", c.Powers.Select(p => $"{GameNames.Of(Capture.IdOf(p))} {p.Amount.ToString(CultureInfo.InvariantCulture)}"));
}
