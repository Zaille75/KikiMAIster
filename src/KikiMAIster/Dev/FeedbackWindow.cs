#if DEV_TOOLS
using System.Globalization;
using System.Text;
using Godot;
using KikiMAIster.Core;
using KikiMAIster.UI;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace KikiMAIster.Dev;

/// Development only (not in public builds): F9 opens a small window to write a bug report or an idea in game. Each report
/// is a folder in "feedback/" next to the DLL: the message, a screenshot taken when F9 was pressed, the game state (hands,
/// piles, enemies, intents, powers, relics, potions), the run's stats and the end of the mod's log.
public static class FeedbackWindow
{
    private static PanelContainer? _panel;
    private static TextEdit? _text;
    private static Label? _status;
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
            var dir = Path.GetDirectoryName(typeof(FeedbackWindow).Assembly.Location);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "feedback");
        }
    }

    public static void Attach(CanvasLayer layer)
    {
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
        var title = Overlay.MakeLabel("KikiMAIster · Signaler (F9)", Overlay.Accent);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("Feedback.Close", Hide);
        header.AddChild(close);
        box.AddChild(header);

        var kinds = new HBoxContainer();
        kinds.AddChild(Overlay.MakeLabel("Type :", Overlay.Text));
        foreach (var (key, label) in new[] { ("bug", "Bug"), ("stat", "Stat fausse"), ("idee", "Idée") })
        {
            var b = Overlay.MakeButton(label);
            b.Pressed += () => Capture.Safe("Feedback.Kind", () =>
            {
                _category = key;
                Refresh();
            });
            CategoryButtons[key] = b;
            kinds.AddChild(b);
        }
        box.AddChild(kinds);

        _text = new TextEdit
        {
            CustomMinimumSize = new Vector2(460, 150), WrapMode = TextEdit.LineWrappingMode.Boundary,
            PlaceholderText = "Ce qui s'est passé, ce que tu attendais (le combat, le tour, la carte…)"
        };
        _text.AddThemeFontSizeOverride("font_size", Overlay.FontSize);
        box.AddChild(_text);

        _withScreenshot = new CheckBox { Text = "Joindre la capture d'écran (prise à l'appui sur F9)", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
        _withScreenshot.AddThemeFontSizeOverride("font_size", Overlay.FontSize);
        Overlay.StyleCheckBox(_withScreenshot);
        box.AddChild(_withScreenshot);

        var actions = new HBoxContainer();
        _status = Overlay.MakeLabel("", Overlay.Muted);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(_status);
        var save = Overlay.MakeButton("Enregistrer");
        save.AddThemeColorOverride("font_color", Overlay.Accent);
        save.Pressed += () => Capture.Safe("Feedback.Save", Save);
        actions.AddChild(save);
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
        Capture.Safe("Feedback.Screenshot", () => _screenshot = _panel.GetViewport().GetTexture().GetImage());
        _state = "";
        Capture.Safe("Feedback.State", () => _state = GameState());
        _status!.Text = "";
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

    private static void Refresh()
    {
        foreach (var (key, b) in CategoryButtons) b.AddThemeColorOverride("font_color", key == _category ? Overlay.Accent : Overlay.Muted);
    }

    private static void Save()
    {
        if (Folder == null || _text == null) return;
        var now = DateTime.Now;
        var dir = Path.Combine(Folder, $"{now:yyyy-MM-dd_HH-mm-ss}_{_category}");
        Directory.CreateDirectory(dir);
        var version = typeof(FeedbackWindow).Assembly.GetName().Version?.ToString(3) ?? "?";
#if GAME_DEFAULT
        const string branch = "main";
#else
        const string branch = "beta";
#endif
        File.WriteAllText(Path.Combine(dir, "message.txt"),
            $"Type: {_category}\nDate: {now:yyyy-MM-dd HH:mm:ss}\nMod: v{version} ({branch})\n\n{_text.Text}\n");
        File.WriteAllText(Path.Combine(dir, "state.txt"), _state);
        Capture.Safe("Feedback.Run", () => File.WriteAllText(Path.Combine(dir, "run.txt"), RunSnapshot.Write("feedback", 0, Capture.Engine)));
        Capture.Safe("Feedback.Log", () =>
        {
            var log = Path.Combine(Path.GetDirectoryName(typeof(FeedbackWindow).Assembly.Location)!, "KikiMAIster.log");
            if (!File.Exists(log)) return;
            using var stream = new FileStream(log, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            File.WriteAllLines(Path.Combine(dir, "log-tail.txt"), lines.TakeLast(600));
        });
        if (_withScreenshot!.ButtonPressed && _screenshot != null)
            Capture.Safe("Feedback.Png", () => _screenshot.SavePng(Path.Combine(dir, "screenshot.png")));
        ModLog.Info($"feedback saved: {dir}");
        _text.Text = "";
        _status!.Text = $"Enregistré ({Directory.GetDirectories(Folder).Length} au total)";
        Hide();
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
#endif
