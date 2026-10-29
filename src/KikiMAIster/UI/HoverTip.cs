using Godot;

namespace KikiMAIster.UI;

/// A hover text shown to the right of the mouse, so it never covers the line being read (v0.17.42, Zaille75: the
/// game's own tooltip opened under the mouse over the trophy names). Used in the trophies window.
public static class HoverTip
{
    private static readonly Vector2 Offset = new(28, -8);
    private static PanelContainer? _popup;
    private static Label? _label;

    /// Shows `text` next to the mouse while it is over `control`.
    public static void Set(Control control, string text)
    {
        control.MouseFilter = Control.MouseFilterEnum.Stop;
        control.MouseEntered += () => Capture.Safe("HoverTip.Show", () => Show(control, text));
        control.MouseExited += () => Capture.Safe("HoverTip.Hide", Hide);
        control.GuiInput += e =>
        {
            if (e is InputEventMouseMotion) Capture.Safe("HoverTip.Move", () => Place(control));
        };
        control.TreeExiting += () => Capture.Safe("HoverTip.Hide", Hide);
    }

    private static void Show(Control control, string text)
    {
        if (text == "") return;
        var layer = LayerOf(control);
        if (layer == null) return;
        if (_popup == null || !GodotObject.IsInstanceValid(_popup))
        {
            _popup = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 100 };
            _popup.AddThemeStyleboxOverride("panel", Overlay.WindowStyle(0.97f));
            _label = Overlay.MakeLabel("", Overlay.Text);
            _label.MouseFilter = Control.MouseFilterEnum.Ignore;
            _popup.AddChild(_label);
        }
        if (_popup.GetParent() != layer)
        {
            _popup.GetParent()?.RemoveChild(_popup);
            layer.AddChild(_popup);
        }
        layer.MoveChild(_popup, -1);
        _label!.Text = text;
        _popup.Size = Vector2.Zero; // shrink to the new text
        _popup.Visible = true;
        Place(control);
    }

    private static void Place(Control control)
    {
        if (_popup is not { Visible: true } || !GodotObject.IsInstanceValid(_popup)) return;
        var screen = control.GetViewportRect().Size;
        var size = _popup.GetCombinedMinimumSize();
        var pos = control.GetGlobalMousePosition() + Offset;
        // Not enough room on the right: to the left of the mouse instead.
        if (pos.X + size.X > screen.X) pos.X = control.GetGlobalMousePosition().X - Offset.X - size.X;
        pos.Y = Mathf.Clamp(pos.Y, 0, Math.Max(0, screen.Y - size.Y));
        _popup.Position = pos;
    }

    private static void Hide()
    {
        if (_popup != null && GodotObject.IsInstanceValid(_popup)) _popup.Visible = false;
    }

    private static CanvasLayer? LayerOf(Node node)
    {
        for (var n = node.GetParent(); n != null; n = n.GetParent())
            if (n is CanvasLayer layer) return layer;
        return null;
    }
}
