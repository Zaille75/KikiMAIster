using Godot;

namespace KikiMAIster.UI;

/// Settings window (spec 02-ui §4), opened by the ⚙ button: language, automatic summary, and which sections and
/// columns the panel shows. Every change is saved at once.
public static class SettingsMenu
{
    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static bool _rebuildPending;

    private static ModConfig Cfg => ModConfig.Current;

    public static void Attach(CanvasLayer layer)
    {
        _panel = new PanelContainer { Visible = false };
        _panel.AddThemeStyleboxOverride("panel", Overlay.WindowStyle(Math.Max(Cfg.Opacity, 0.92f)));
        _content = new VBoxContainer();
        _content.AddThemeConstantOverride("separation", 2);
        _panel.AddChild(_content);
        layer.AddChild(_panel);
        var timer = new Godot.Timer { WaitTime = 0.2, Autostart = true };
        timer.Timeout += () => Capture.Safe("SettingsMenu.Tick", () =>
        {
            if (!_rebuildPending || _panel is not { Visible: true }) return;
            _rebuildPending = false;
            Rebuild();
        });
        layer.AddChild(timer);
    }

    public static void Hide()
    {
        if (_panel != null && _panel.Visible) _panel.Visible = false;
    }

    public static void Toggle()
    {
        if (_panel == null) return;
        if (_panel.Visible)
        {
            _panel.Visible = false;
            return;
        }
        Rebuild();
        _panel.Visible = true;
        // Next to the panel, on its right (or on its left when there is no room).
        var screen = _panel.GetViewportRect().Size;
        var rect = Overlay.PanelRect ?? new Rect2(20, 160, 330, 100);
        var x = rect.End.X + 8;
        if (x + 360 > screen.X) x = Math.Max(0, rect.Position.X - 368);
        _panel.Position = new Vector2(x, Math.Clamp(rect.Position.Y, 0, Math.Max(0, screen.Y - 500)));
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

        var header = new HBoxContainer();
        var title = Overlay.MakeLabel(Loc.T("settings.title"), Overlay.Accent);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        header.AddChild(title);
        var close = Overlay.MakeButton("✕");
        close.Pressed += () => Capture.Safe("SettingsMenu.Close", () => _panel.Visible = false);
        header.AddChild(close);
        _content.AddChild(header);

        // Language: auto (game language) / English / French.
        var lang = new HBoxContainer();
        lang.AddChild(Overlay.MakeLabel(Loc.T("settings.language"), Overlay.Text));
        foreach (var (code, label) in new[] { ("auto", Loc.T("settings.auto")), ("en", "English"), ("fr", "Français") })
        {
            var b = Overlay.MakeButton(label);
            b.AddThemeColorOverride("font_color", Cfg.Language == code ? Overlay.Accent : Overlay.Muted);
            b.Pressed += () => Capture.Safe("SettingsMenu.Language", () =>
            {
                Cfg.Language = code;
                Changed();
            });
            lang.AddChild(b);
        }
        _content.AddChild(lang);

        _content.AddChild(Check(Loc.T("summary.auto"), Cfg.AutoSummary, on =>
        {
            Cfg.AutoSummary = on;
            Cfg.Save();
        }));

        _content.AddChild(Overlay.MakeLabel(Loc.T("settings.shown"), Overlay.Accent));
        foreach (var (section, columns) in Overlay.Hideable)
        {
            var sectionKey = $"section:{section}";
            var visible = !Cfg.Hidden.Contains(sectionKey);
            _content.AddChild(Check(Loc.T($"section.{section}"), visible, on => Set(sectionKey, on)));
            if (!visible) continue;
            // A grid, not a flow container: its size is known at once, so the window's background fits the text.
            var row = new HBoxContainer();
            row.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
            var grid = new GridContainer { Columns = 3 };
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 0);
            foreach (var col in columns)
            {
                var key = $"col:{section}:{col}";
                grid.AddChild(Check(Loc.T(col), !Cfg.Hidden.Contains(key), on => Set(key, on), small: true));
            }
            row.AddChild(grid);
            _content.AddChild(row);
        }
    }

    private static void Set(string hiddenKey, bool visible)
    {
        if (visible) Cfg.Hidden.Remove(hiddenKey);
        else Cfg.Hidden.Add(hiddenKey);
        Changed();
    }

    /// Saves, refreshes the panel and rebuilds this window on the next tick (not from inside its own signal).
    private static void Changed()
    {
        Cfg.Save();
        Overlay.Invalidate();
        CombatSummary.Refresh();
        RunSummary.Refresh();
        _rebuildPending = true;
    }

    private static CheckBox Check(string text, bool on, Action<bool> toggled, bool small = false)
    {
        var c = new CheckBox { Text = text, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None };
        c.AddThemeFontSizeOverride("font_size", small ? Overlay.FontSize - 1 : Overlay.FontSize);
        c.AddThemeColorOverride("font_color", small ? Overlay.Muted : Overlay.Text);
        c.Toggled += value => Capture.Safe("SettingsMenu.Toggle", () => toggled(value));
        return c;
    }
}
