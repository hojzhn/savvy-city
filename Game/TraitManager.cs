using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Timer = Godot.Timer;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Game;

/// <summary>
/// Every trait a pawn can have: how it is determined, what it depends on,
/// how it comes out across many pawns, and an editor for its rules.
/// Edits go to a draft copy of the data; Save validates and writes the YAML.
/// </summary>
public partial class TraitManager : HSplitContainer
{
    const int SampleSize = 2000;
    static readonly Regex IdPattern = new("^[A-Za-z0-9_]+$");

    public DataHost Host { get; init; } = null!;

    // Draft state. _family and _config are private copies that edits change.
    FamilyFile _family = new();
    Config _config = new();
    LocationsFile _locations = new();
    YouWillDate.Sim.Life.LifeFile? _life;
    YouWillDate.Sim.Adult.AdultFile? _adult;
    bool _dirty;
    GameData? _draftData;          // null while the draft is invalid
    TraitCatalog? _catalog;        // last valid catalog (draft if valid, else previous)
    string? _draftError;
    string? _selected;             // schema key, or "group:<index>"
    string _filter = "";

    // Population samples: saved data, and the draft while editing.
    Dictionary<string, FactStats>? _savedStats, _draftStats;
    CancellationTokenSource? _savedCancel, _draftCancel;
    string? _statsError;

    LineEdit _search = null!;
    Tree _tree = null!;
    Label _saveState = null!;
    Button _save = null!, _discard = null!;
    Label _banner = null!;
    VBoxContainer _detail = null!;
    Timer _debounce = null!;
    ConfirmationDialog _confirm = null!;
    Action? _pendingConfirm;
    bool _building;

    public override void _Ready()
    {
        BuildUi();
        Host.Reloaded += OnReloaded;
        if (Host.Data is not null) OnReloaded();
    }

    public override void _ExitTree() => Host.Reloaded -= OnReloaded;

    /// <summary>Show a trait, by schema key (as from the pawn inspector).</summary>
    public void SelectTrait(string key)
    {
        _selected = key;
        _filter = "";
        _search.Text = "";
        BuildTree();
        ShowSelected();
    }

    // ---------------------------------------------------------------- data

    void OnReloaded()
    {
        ComputeStats(draft: false);
        if (_dirty)
        {
            SetBanner("The data files changed on disk while you have unsaved edits. " +
                      "Save will overwrite them; Discard loads the new files.", Ui.Error);
            return;
        }
        ResetDraft();
    }

    void ResetDraft()
    {
        var data = Host.Data!;
        _family = YamlIO.Clone(data.FamilySource);
        _config = YamlIO.Clone(data.Config);
        _locations = YamlIO.Clone(data.LocationsSource);
        _life = data.LifeSource is null ? null : YamlIO.Clone(data.LifeSource);
        _adult = data.AdultSource is null ? null : YamlIO.Clone(data.AdultSource);
        _draftData = data;
        _catalog = Host.Catalog;
        _draftError = null;
        _dirty = false;
        _draftStats = null;
        SetBanner("");
        UpdateSaveBar();
        BuildTree();
        ShowSelected();
    }

    /// <summary>
    /// Call after every edit to the draft.
    /// structural: the tree or editor layout changed, so rebuild them.
    /// rules: the edit can change generated pawns, so resample.
    /// </summary>
    void Changed(bool structural, bool rules = true)
    {
        _dirty = true;
        Validate();
        UpdateSaveBar();
        if (structural)
        {
            BuildTree();
            ShowSelected();
        }
        else
        {
            RefreshInfo();
        }
        if (rules && _draftData is not null) _debounce.Start();
    }

    void Validate()
    {
        var data = Host.Data!;
        try
        {
            _draftData = GameData.FromFiles(YamlIO.Clone(_config), YamlIO.Clone(_locations), data.ErasSource, YamlIO.Clone(_family),
                _life is null ? null : YamlIO.Clone(_life), _adult is null ? null : YamlIO.Clone(_adult));
            _catalog = new TraitCatalog(_draftData);
            _draftError = null;
            SetBanner("");
        }
        catch (DataException e)
        {
            _draftData = null;
            _draftError = e.Message;
            SetBanner("Can't save yet: " + e.Message, Ui.Error);
        }
    }

    void ComputeStats(bool draft)
    {
        var gen = draft ? (_draftData is null ? null : new PawnGenerator(_draftData)) : Host.Generator;
        if (gen is null) return;

        ref var slot = ref draft ? ref _draftCancel : ref _savedCancel;
        slot?.Cancel();
        var cancel = slot = new CancellationTokenSource();

        Task.Run(() => PopulationStats.Run(gen, SampleSize, null, cancel.Token), cancel.Token)
            .ContinueWith(t =>
            {
                if (cancel.IsCancellationRequested) return;
                var result = t.IsCompletedSuccessfully ? t.Result : null;
                string? error = t.Exception?.InnerException?.Message;
                Callable.From(() => ApplyStats(draft, cancel, result, error)).CallDeferred();
            });
    }

    void ApplyStats(bool draft, CancellationTokenSource cancel, Dictionary<string, FactStats>? result, string? error)
    {
        if (cancel.IsCancellationRequested) return;
        _statsError = error;
        if (draft) _draftStats = result;
        else _savedStats = result;
        RefreshInfo();
    }

    void Save()
    {
        if (_draftData is null || Host.Data is null) return;
        var files = new List<(string, string)>();
        if (YamlIO.Write(_family) != YamlIO.Write(Host.Data.FamilySource))
            files.Add(("family.yaml", YamlIO.Write(_family, YamlIO.LeadingComments(Host.ReadFile("family.yaml")))));
        if (YamlIO.Write(_config) != YamlIO.Write(Host.Data.Config))
            files.Add(("config.yaml", YamlIO.Write(_config, YamlIO.LeadingComments(Host.ReadFile("config.yaml")))));
        if (YamlIO.Write(_locations) != YamlIO.Write(Host.Data.LocationsSource))
            files.Add(("locations.yaml", YamlIO.Write(_locations, YamlIO.LeadingComments(Host.ReadFile("locations.yaml")))));
        // Renames reach into life.yaml too.
        if (_life is not null && Host.Data.LifeSource is not null && YamlIO.Write(_life) != YamlIO.Write(Host.Data.LifeSource))
            files.Add(("life.yaml", YamlIO.Write(_life, YamlIO.LeadingComments(Host.ReadFile("life.yaml")))));
        if (_adult is not null && Host.Data.AdultSource is not null && YamlIO.Write(_adult) != YamlIO.Write(Host.Data.AdultSource))
            files.Add(("adult.yaml", YamlIO.Write(_adult, YamlIO.LeadingComments(Host.ReadFile("adult.yaml")))));

        _dirty = false;
        try
        {
            if (files.Count > 0) Host.WriteFiles(files.ToArray()); // reloads, which resets the draft
            else ResetDraft();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dirty = true;
            SetBanner("Couldn't save: " + e.Message, Ui.Error);
            UpdateSaveBar();
        }
    }

    void Discard()
    {
        _draftCancel?.Cancel();
        _dirty = false;
        ResetDraft();
    }

    void UpdateSaveBar()
    {
        _save.Disabled = !_dirty || _draftData is null;
        _discard.Disabled = !_dirty;
        _saveState.Text = !_dirty ? "All changes saved."
            : _draftData is null ? "Unsaved changes (fix the error before saving)."
            : "Unsaved changes.";
        Ui.SetColor(_saveState, _dirty ? Ui.Override : Ui.Dim);
    }

    void SetBanner(string text, Color? color = null)
    {
        _banner.Text = text;
        _banner.Visible = text.Length > 0;
        Ui.SetColor(_banner, color);
    }

    // ---------------------------------------------------------------- tree

    void BuildTree()
    {
        _building = true;
        _tree.Clear();
        var root = _tree.CreateItem();
        TreeItem? selected = null;

        TreeItem Part(string title)
        {
            var part = _tree.CreateItem(root);
            part.SetText(0, title.ToUpperInvariant());
            part.SetSelectable(0, false);
            part.SetCustomColor(0, Ui.Changed);
            return part;
        }
        var startPart = Part("Starting point");
        var lifePart = Part("Life");
        var adultPart = Part("Adult traits");
        TreeItem current = startPart;

        TreeItem? Section(string title, string? meta)
        {
            var head = _tree.CreateItem(current);
            head.SetText(0, title);
            if (meta is null) head.SetSelectable(0, false);
            else
            {
                head.SetMetadata(0, meta);
                if (meta == _selected) selected = head;
            }
            return head;
        }

        void Row(TreeItem section, string key, string label, string? tooltip = null)
        {
            if (_filter.Length > 0 && !label.Contains(_filter, StringComparison.OrdinalIgnoreCase)
                                   && !key.Contains(_filter, StringComparison.OrdinalIgnoreCase)) return;
            var row = _tree.CreateItem(section);
            row.SetText(0, Ui.Pretty(label));
            row.SetMetadata(0, key);
            if (tooltip is not null) row.SetTooltipText(0, tooltip);
            if (key == _selected) selected = row;
        }

        var seed = Section("Seed", null)!;
        Row(seed, "pawn.current_city", "current city");
        Row(seed, "year", "birth year");
        Row(seed, "pawn.birth_location", "birth location");
        Row(seed, "pawn.born_locally", "born locally");
        Row(seed, "pawn.sex", "sex");

        var temperament = Section("Temperament", null)!;
        foreach (var t in _config.Temperament.Traits) Row(temperament, $"pawn.{t}", t);

        var context = Section("Birth context", null)!;
        foreach (var field in ContextSchema.Values.Keys) Row(context, $"context.{field}", field);

        for (int g = 0; g < _family.Groups.Count; g++)
        {
            var group = _family.Groups[g];
            var head = Section(Ui.Pretty(group.Name) + (group.PerParent ? "  (each parent)" : ""), $"group:{g}")!;
            foreach (var item in group.Items)
                Row(head, FamilyEdits.ItemKey(group, item), item.Id, item.Note);
        }

        // Life and adult traits: read-only here, edited in life.yaml and adult.yaml.
        if (_catalog is not null)
        {
            foreach (var (kind, part) in new[] { (TraitKind.Life, lifePart), (TraitKind.Adult, adultPart) })
            {
                current = part;
                var sections = new Dictionary<string, TreeItem>();
                foreach (var info in _catalog.Traits.Where(t => t.Kind == kind))
                {
                    if (!sections.TryGetValue(info.Section, out var head))
                    {
                        head = Section(info.Section, null)!;
                        head.Collapsed = info.Section is "Life clock" or "Adult inputs";
                        sections[info.Section] = head;
                    }
                    Row(head, info.Key, info.Label, info.Note);
                }
            }
        }

        // Hide sections (and parts) the filter emptied.
        if (_filter.Length > 0)
            foreach (var part in root.GetChildren())
            {
                foreach (var head in part.GetChildren())
                    head.Visible = head.GetChildCount() > 0;
                part.Visible = part.GetChildren().Any(h => h.Visible);
            }

        if (selected is not null)
        {
            selected.Select(0);
            _tree.ScrollToItem(selected);
        }
        _building = false;
    }

    void OnTreeSelected()
    {
        var meta = _tree.GetSelected()?.GetMetadata(0);
        if (meta is null || meta.Value.VariantType == Variant.Type.Nil) return;
        _selected = meta.Value.AsString();
        ShowSelected();
    }

    // ------------------------------------------------------------ toolbar

    (GroupDto Group, ItemDto Item)? SelectedItem() =>
        _selected is null ? null : FamilyEdits.FindItem(_family, _selected);

    GroupDto? SelectedGroup() =>
        _selected is not null && _selected.StartsWith("group:") && int.TryParse(_selected[6..], out int g) && g < _family.Groups.Count
            ? _family.Groups[g]
            : SelectedItem()?.Group;

    string? SelectedTemperamentTrait() =>
        _selected is not null && _selected.StartsWith("pawn.") && _config.Temperament.Traits.Contains(_selected[5..])
            ? _selected[5..]
            : null;

    void AddTrait()
    {
        if (SelectedTemperamentTrait() is not null)
        {
            var traits = _config.Temperament.Traits;
            string name = "new_trait";
            for (int n = 2; traits.Contains(name); n++) name = $"new_trait_{n}";
            traits.Add(name);
            _selected = $"pawn.{name}";
            Changed(structural: true);
            return;
        }

        var group = SelectedGroup();
        if (group is null)
        {
            SetBanner("Select a group or trait under it to add a trait there. (Seed and context traits are fixed.)", Ui.Override);
            return;
        }
        var item = new ItemDto
        {
            Id = FamilyEdits.UniqueItemId(_family, "new_trait"),
            Values = ["option_a", "option_b"],
        };
        int at = SelectedItem() is { } sel ? group.Items.IndexOf(sel.Item) + 1 : group.Items.Count;
        group.Items.Insert(at, item);
        _selected = FamilyEdits.ItemKey(group, item);
        Changed(structural: true);
    }

    void AddGroup()
    {
        var names = _family.Groups.Select(g => g.Name).ToHashSet();
        string name = "new_group";
        for (int n = 2; names.Contains(name); n++) name = $"new_group_{n}";
        var item = new ItemDto { Id = FamilyEdits.UniqueItemId(_family, "new_trait"), Values = ["option_a", "option_b"] };
        _family.Groups.Add(new GroupDto { Name = name, Items = [item] });
        _selected = $"group:{_family.Groups.Count - 1}";
        Changed(structural: true);
    }

    void Move(int delta)
    {
        if (SelectedTemperamentTrait() is { } t)
        {
            var traits = _config.Temperament.Traits;
            int i = traits.IndexOf(t), j = i + delta;
            if (j < 0 || j >= traits.Count) return;
            (traits[i], traits[j]) = (traits[j], traits[i]);
            Changed(structural: true);
            return;
        }
        if (SelectedItem() is not { } sel) return;
        var items = sel.Group.Items;
        int a = items.IndexOf(sel.Item), b = a + delta;
        if (b < 0 || b >= items.Count) return;
        (items[a], items[b]) = (items[b], items[a]);
        // Order matters: an item can only read items above it, so this can break rules.
        Changed(structural: true);
    }

    void Delete()
    {
        if (SelectedTemperamentTrait() is { } t)
        {
            var refs = FamilyEdits.ReferencesTo(_family, $"pawn.{t}", _life, _adult)
                .Concat(FamilyEdits.ReferencesTo(_family, $"z.{t}", _life, _adult)).ToList();
            Confirm($"Delete the temperament trait '{t}'?" + RefsWarning(refs), () =>
            {
                _config.Temperament.Traits.Remove(t);
                _config.Temperament.Correlations.RemoveAll(c => c.A == t || c.B == t);
                _selected = null;
                Changed(structural: true);
            });
            return;
        }

        if (SelectedItem() is { } sel)
        {
            string key = FamilyEdits.ItemKey(sel.Group, sel.Item);
            var refs = FamilyEdits.ReferencesTo(_family, key, _life, _adult);
            Confirm($"Delete the trait '{key}'?" + RefsWarning(refs), () =>
            {
                sel.Group.Items.Remove(sel.Item);
                _selected = null;
                Changed(structural: true);
            });
            return;
        }

        if (SelectedGroup() is { } group)
        {
            if (group.Items.Count > 0)
            {
                SetBanner($"Group '{group.Name}' still has traits. Delete them first.", Ui.Override);
                return;
            }
            _family.Groups.Remove(group);
            _selected = null;
            Changed(structural: true);
            return;
        }

        SetBanner("Seed and context traits can't be deleted.", Ui.Override);
    }

    static string RefsWarning(List<string> refs) => refs.Count == 0
        ? "\n\nNothing else reads it."
        : $"\n\n{refs.Count} rule(s) read it and will show an error until you fix them:\n  "
          + string.Join("\n  ", refs.Take(12)) + (refs.Count > 12 ? "\n  ..." : "");

    void Confirm(string text, Action onYes)
    {
        _pendingConfirm = onYes;
        _confirm.DialogText = text;
        _confirm.PopupCentered(new Vector2I(560, 0));
    }

    // ------------------------------------------------------------ layout

    void BuildUi()
    {
        // Left: search, tree, toolbar
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        AddChild(left);
        _search = new LineEdit { PlaceholderText = "Filter traits...", ClearButtonEnabled = true };
        _search.TextChanged += text => { _filter = text.Trim(); BuildTree(); };
        left.AddChild(_search);

        _tree = new Tree { HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _tree.ItemSelected += () => { if (!_building) OnTreeSelected(); };
        left.AddChild(_tree);

        var tools = new HFlowContainer();
        left.AddChild(tools);
        tools.AddChild(Ui.Button("+ Trait", AddTrait, "Add a trait after the selected one (or into the selected group)"));
        tools.AddChild(Ui.Button("+ Group", AddGroup, "Add a new group of traits at the end"));
        tools.AddChild(Ui.Button("Up", () => Move(-1), "Move up. Traits can only read traits above them."));
        tools.AddChild(Ui.Button("Down", () => Move(1), "Move down. Traits can only read traits above them."));
        tools.AddChild(Ui.Button("Delete", Delete));

        // Right: save bar, banner, scrolling detail
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        AddChild(right);

        var saveBar = new HBoxContainer();
        right.AddChild(saveBar);
        _saveState = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        saveBar.AddChild(_saveState);
        _discard = Ui.Button("Discard", Discard, "Throw away unsaved edits and reload the files");
        saveBar.AddChild(_discard);
        _save = Ui.Button("Save", Save, "Write the changed data files (the previous version of each is kept as .bak)");
        saveBar.AddChild(_save);

        _banner = Ui.Text("");
        _banner.Visible = false;
        right.AddChild(_banner);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        right.AddChild(scroll);
        _detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _detail.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(Ui.Pad(_detail, 4));

        _debounce = new Timer { WaitTime = 0.5, OneShot = true };
        _debounce.Timeout += () => ComputeStats(draft: true);
        AddChild(_debounce);

        _confirm = new ConfirmationDialog { Title = "Are you sure?", DialogAutowrap = true };
        _confirm.Confirmed += () => { _pendingConfirm?.Invoke(); _pendingConfirm = null; };
        AddChild(_confirm);

        UpdateSaveBar();
    }
}
