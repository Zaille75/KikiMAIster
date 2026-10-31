using Godot;

namespace KikiMAIster.UI;

/// Settings window (spec 02-ui §4), opened by the ⚙ button: language, automatic summary, and which sections and
/// columns the panel shows. Every change is saved at once.
public static class SettingsMenu
{
    private static PanelContainer? _panel;
    private static VBoxContainer? _content;
    private static bool _rebuildPending;
    private static bool _dragging;

    private static ModConfig Cfg => ModConfig.Current;

    public static void Attach(CanvasLayer layer)
    {
        // Dragged by its background (v0.17.12); where it was left is remembered.
        _panel = new PanelContainer { Visible = false, MouseDefaultCursorShape = Control.CursorShape.Move };
        _panel.AddThemeStyleboxOverride("panel", Overlay.WindowStyle(Math.Max(Cfg.Opacity, 0.92f)));
        _panel.GuiInput += e => Capture.Safe("SettingsMenu.Drag", () =>
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                    _dragging = mb.Pressed;
                    if (!mb.Pressed)
                    {
                        Cfg.SettingsX = _panel.Position.X;
                        Cfg.SettingsY = _panel.Position.Y;
                        Cfg.Save();
                    }
                    break;
                case InputEventMouseMotion mm when _dragging:
                    _panel.Position += mm.Relative;
                    break;
            }
        });
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

    /// Opened from the game's Mods screen (v0.18.9, Zaille75): shown outside a run until it is closed.
    public static bool FromMods { get; private set; }

    /// The hotkey being chosen ("panel", "compositions", "report"), waiting for a key press.
    private static string? _capturing;

    public static void Hide()
    {
        FromMods = false;
        _capturing = null;
        if (_panel != null && _panel.Visible) _panel.Visible = false;
    }

    public static void OpenFromMods()
    {
        if (_panel == null) return;
        FromMods = true;
        if (!_panel.Visible) Toggle();
        else _panel.MoveToFront();
    }

    /// A key pressed while a hotkey is being chosen: it becomes that hotkey (Échap cancels). True when the key was taken.
    public static bool TryCaptureKey(Key key)
    {
        if (_capturing == null) return false;
        var which = _capturing;
        _capturing = null;
        if (key != Key.Escape)
        {
            var name = key.ToString();
            switch (which)
            {
                case "panel": Cfg.KeyPanel = name; break;
                case "compositions": Cfg.KeyCompositions = name; break;
                case "report": Cfg.KeyReport = name; break;
            }
            Cfg.Save();
        }
        Changed();
        return true;
    }

    public static void Toggle()
    {
        if (_panel == null) return;
        if (_panel.Visible)
        {
            Hide();
            return;
        }
        Rebuild();
        _panel.Visible = true;
        _panel.MoveToFront();
        var screen = _panel.GetViewportRect().Size;
        if (Cfg.SettingsX >= 0)
        {
            _panel.Position = new Vector2(Math.Clamp(Cfg.SettingsX, 0, Math.Max(0, screen.X - 360)), Math.Clamp(Cfg.SettingsY, 0, Math.Max(0, screen.Y - 100)));
            return;
        }
        // Next to the panel, on its right (or on its left when there is no room).
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
        close.Pressed += () => Capture.Safe("SettingsMenu.Close", Hide);
        header.AddChild(close);
        _content.AddChild(header);

        // Language: English / French (the mod only speaks these two; until one is picked it follows the game).
        var lang = new HBoxContainer();
        lang.AddChild(Overlay.MakeLabel(Loc.T("settings.language"), Overlay.Text));
        foreach (var (code, label) in new[] { ("en", "English"), ("fr", "Français") })
        {
            var b = Overlay.MakeButton(label);
            b.AddThemeColorOverride("font_color", Loc.French == (code == "fr") ? Overlay.Accent : Overlay.Muted);
            b.Pressed += () => Capture.Safe("SettingsMenu.Language", () =>
            {
                Cfg.Language = code;
                Changed();
            });
            lang.AddChild(b);
        }
        _content.AddChild(lang);

        // Date format (v0.18.5, Zaille75): DD/MM/YYYY by default.
        var dates = new HBoxContainer();
        dates.AddChild(Overlay.MakeLabel(Loc.T("settings.dateFormat"), Overlay.Text));
        foreach (var (code, label) in new[] { ("dmy", "DD/MM/YYYY"), ("ymd", "YYYY/MM/DD"), ("ydm", "YYYY/DD/MM") })
        {
            var b = Overlay.MakeButton(label);
            b.AddThemeColorOverride("font_color", Cfg.DateFormat == code ? Overlay.Accent : Overlay.Muted);
            b.Pressed += () => Capture.Safe("SettingsMenu.DateFormat", () =>
            {
                Cfg.DateFormat = code;
                Changed();
            });
            dates.AddChild(b);
        }
        _content.AddChild(dates);

        // Hotkeys (v0.18.9, Zaille75): click one, then press the new key (Échap cancels).
        var keys = new HBoxContainer();
        keys.AddChild(Overlay.MakeLabel(Loc.T("settings.hotkeys"), Overlay.Text));
        foreach (var (which, label, current) in new[] { ("panel", "settings.keyPanel", Cfg.KeyPanel),
                     ("compositions", "settings.keyCompositions", Cfg.KeyCompositions), ("report", "settings.keyReport", Cfg.KeyReport) })
        {
            var b = Overlay.MakeButton(string.Format(Loc.T("settings.keyFormat"), Loc.T(label),
                _capturing == which ? Loc.T("settings.pressKey") : current));
            b.AddThemeColorOverride("font_color", _capturing == which ? Overlay.Accent : Overlay.Muted);
            b.Pressed += () => Capture.Safe("SettingsMenu.Hotkey", () =>
            {
                _capturing = which;
                Rebuild();
            });
            keys.AddChild(b);
        }
        _content.AddChild(keys);

        _content.AddChild(Check(Loc.T("summary.auto"), Cfg.AutoSummary, on =>
        {
            Cfg.AutoSummary = on;
            Cfg.Save();
        }));

        _content.AddChild(Check(Loc.T("settings.hideZero"), Cfg.HideZeroColumns, on =>
        {
            Cfg.HideZeroColumns = on;
            Changed();
        }));
        _content.AddChild(Check(Loc.T("settings.groupLine"), Cfg.ShowGroupLine, on =>
        {
            Cfg.ShowGroupLine = on;
            Changed();
        }));

        _content.AddChild(Overlay.MakeLabel(Loc.T("settings.shown"), Overlay.Accent));
        // Solo (v0.18.2): the co-op sections and ally columns never show, so they are not offered.
        var solo = Patches.CombatFlowPatches.PlayerCount == 1;
        foreach (var (section, allColumns) in Overlay.Hideable)
        {
            if (solo && Overlay.CoopSections.Contains(section)) continue;
            var columns = solo ? allColumns.Where(c => !Overlay.AllyColumns.Contains(c)).ToArray() : allColumns;
            var sectionKey = $"section:{section}";
            var visible = !Cfg.Hidden.Contains(sectionKey);
            // The section's checkbox, then "own window" (v0.17.0: a detached section can be placed anywhere).
            var line = new HBoxContainer();
            line.AddChild(Check(Loc.T($"section.{section}"), visible, on => Set(sectionKey, on)));
            if (visible)
            {
                var detach = Check(Loc.T("settings.detach"), Cfg.Detached.Contains(section), on =>
                {
                    if (on) Cfg.Detached.Add(section);
                    else Cfg.Detached.Remove(section);
                    Changed();
                }, small: true);
                detach.TooltipText = Loc.T("settings.detachTip");
                line.AddChild(detach);
            }
            _content.AddChild(line);
            if (!visible || columns.Length == 0) continue;
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

        // Reports (v0.18.0): the same window as F9, for players who do not know the key.
        var report = Overlay.MakeButton(Loc.T("report.open"));
        report.Alignment = HorizontalAlignment.Left;
        report.AddThemeColorOverride("font_color", Overlay.Accent);
        report.Pressed += () => Capture.Safe("SettingsMenu.Report", FeedbackWindow.Toggle);
        _content.AddChild(report);
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
        CompositionWindow.Refresh();
        _rebuildPending = true;
    }

    private static CheckBox Check(string text, bool on, Action<bool> toggled, bool small = false)
    {
        var c = new CheckBox { Text = text, ButtonPressed = on, FocusMode = Control.FocusModeEnum.None };
        c.AddThemeFontSizeOverride("font_size", small ? Overlay.FontSize - 1 : Overlay.FontSize);
        c.AddThemeColorOverride("font_color", small ? Overlay.Muted : Overlay.Text);
        Overlay.StyleCheckBox(c);
        c.Toggled += value => Capture.Safe("SettingsMenu.Toggle", () => toggled(value));
        return c;
    }
}
