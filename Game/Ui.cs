using System;
using Godot;

namespace YouWillDate.Game;

/// <summary>Small helpers for building UI in code.</summary>
public static class Ui
{
    public static readonly Color Override = new(1f, 0.6f, 0.2f);
    public static readonly Color Changed = new(0.45f, 0.75f, 1f);
    public static readonly Color Error = new(1f, 0.4f, 0.4f);
    public static readonly Color Good = new(0.5f, 0.9f, 0.5f);
    public static readonly Color Dim = new(1f, 1f, 1f, 0.6f);

    public static readonly Font Mono = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New", "monospace"] };

    public static Button Button(string text, Action onPressed, string? tooltip = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "" };
        b.Pressed += onPressed;
        return b;
    }

    public static Label Heading(string text, int size = 16)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        return l;
    }

    public static Label Text(string text, Color? color = null)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (color is Color c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    public static void SetColor(Label label, Color? color)
    {
        if (color is Color c) label.AddThemeColorOverride("font_color", c);
        else label.RemoveThemeColorOverride("font_color");
    }

    public static MarginContainer Pad(Control child, int pad)
    {
        var m = new MarginContainer();
        foreach (var side in new[] { "left", "top", "right", "bottom" })
            m.AddThemeConstantOverride($"margin_{side}", pad);
        m.AddChild(child);
        return m;
    }

    public static string Pretty(string id) => id.Replace('_', ' ');

    public static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
