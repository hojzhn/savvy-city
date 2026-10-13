using System.Linq;
using Godot;

namespace YouWillDate.Game;

/// <summary>Debug tools shell: tabs for the pawn inspector and the trait manager, plus a data status bar.</summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        var host = new DataHost { Name = "DataHost" };

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        var margin = Ui.Pad(root, 16);
        margin.SetAnchorsPreset(LayoutPreset.FullRect);

        var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(tabs);
        var inspector = new PawnInspector { Host = host, Name = "Pawn inspector" };
        var traits = new TraitManager { Host = host, Name = "Traits" };
        tabs.AddChild(Ui.Pad(inspector, 8));
        tabs.AddChild(Ui.Pad(traits, 8));
        tabs.SetTabTitle(0, "Pawn inspector");
        tabs.SetTabTitle(1, "Traits");
        inspector.EditTraitRequested += key =>
        {
            tabs.CurrentTab = 1;
            traits.SelectTrait(key);
        };

        var footer = new HBoxContainer();
        root.AddChild(footer);
        var status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        footer.AddChild(status);
        var autoReload = new CheckBox
        {
            Text = "Auto-reload", ButtonPressed = true,
            TooltipText = "Reload the YAML files in Data/ whenever one is saved",
        };
        autoReload.Toggled += on => host.AutoReload = on;
        footer.AddChild(autoReload);
        footer.AddChild(Ui.Button("Reload data", () => host.ReloadIfChanged(force: true)));

        host.StatusChanged += (message, error) =>
        {
            status.Text = message;
            Ui.SetColor(status, error ? Ui.Error : Ui.Dim);
        };

        AddChild(margin);
        // Added last: loading the data raises Reloaded, which the tabs listen for.
        AddChild(host);
    }
}
