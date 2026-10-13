using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using YouWillDate.Sim;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Game;

/// <summary>
/// One pawn at a time. Pick a seed, click any value to see how it was rolled,
/// override values to see what changes downstream.
/// </summary>
public partial class PawnInspector : VBoxContainer
{
    static readonly Dictionary<string, string> SectionTitles = new()
    {
        ["pawn"] = "Pawn", ["context"] = "Context", ["mother"] = "Mother", ["father"] = "Father",
        ["parents"] = "Parents match?", ["household"] = "Household",
        ["surroundings"] = "Surroundings", ["climate"] = "Home climate",
    };

    public DataHost Host { get; init; } = null!;

    /// <summary>Raised with a schema key when the user asks to edit the selected trait.</summary>
    public event Action<string>? EditTraitRequested;

    readonly Facts _overrides = new();
    ulong _seed;
    Pawn? _pawn, _baseline;
    string? _selectedKey;
    bool _updating;

    LineEdit _seedInput = null!;
    Tree _tree = null!;
    Label _detailTitle = null!;
    HBoxContainer _overrideRow = null!;
    OptionButton _valuePicker = null!;
    SpinBox _numberInput = null!;
    Button _clearOne = null!;
    RichTextLabel _detail = null!;
    Label _status = null!;

    public override void _Ready()
    {
        BuildUi();
        _seed = (ulong)Random.Shared.NextInt64();
        _seedInput.Text = _seed.ToString();
        Host.Reloaded += OnDataReloaded;
        if (Host.Data is not null) OnDataReloaded();
    }

    public override void _ExitTree() => Host.Reloaded -= OnDataReloaded;

    void OnDataReloaded()
    {
        var dropped = DropInvalidOverrides(Host.Data!);
        Regenerate();
        if (dropped.Count > 0)
            SetStatus($"Dropped overrides that no longer fit the data: {string.Join(", ", dropped)}.", error: true);
    }

    List<string> DropInvalidOverrides(GameData data)
    {
        var dropped = new List<string>();
        foreach (var key in _overrides.Categorical.Keys.Concat(_overrides.Numeric.Keys).ToList())
        {
            var single = new Facts();
            if (_overrides.Cat(key) is string s) single.Set(key, s);
            else single.Set(key, _overrides.Num(key)!.Value);
            try { data.CheckOverrides(single); }
            catch (ArgumentException)
            {
                _overrides.Remove(key);
                dropped.Add(key);
            }
        }
        return dropped;
    }

    void Regenerate()
    {
        if (Host.Generator is not { } gen) return;
        try
        {
            _pawn = gen.Generate(_seed, new GenerateOptions { Overrides = _overrides, Trace = true });
            _baseline = _overrides.Count > 0 ? gen.Generate(_seed) : null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            SetStatus(e.Message, error: true);
            return;
        }
        BuildTree();
        ShowDetail();
        UpdateSummary();
    }

    // ------------------------------------------------------------ actions

    void SetSeed(ulong seed)
    {
        _seed = seed;
        _seedInput.Text = seed.ToString();
        Regenerate();
    }

    void OnSeedEntered()
    {
        if (ulong.TryParse(_seedInput.Text.Trim(), out ulong seed)) SetSeed(seed);
        else SetStatus("The seed must be a whole number.", error: true);
    }

    void SetOverride(string key, string? text, double? number)
    {
        _overrides.Remove(key);
        if (text is not null) _overrides.Set(key, text);
        if (number is double n) _overrides.Set(key, n);
        Regenerate();
    }

    void OnTreeSelected()
    {
        var meta = _tree.GetSelected()?.GetMetadata(0);
        if (meta is null || meta.Value.VariantType == Variant.Type.Nil) return;
        _selectedKey = meta.Value.AsString();
        ShowDetail();
    }

    // ---------------------------------------------------------------- view

    void BuildTree()
    {
        _updating = true;
        _tree.Clear();
        var root = _tree.CreateItem();
        var sections = new Dictionary<string, TreeItem>();
        TreeItem? selected = null;

        foreach (var t in _pawn!.Trace!)
        {
            string section = t.Key == "year" ? "pawn" : t.Key[..t.Key.IndexOf('.')];
            if (!sections.TryGetValue(section, out var head))
            {
                head = _tree.CreateItem(root);
                head.SetText(0, SectionTitles.GetValueOrDefault(section, Capitalize(section)));
                head.SetSelectable(0, false);
                head.SetSelectable(1, false);
                head.Collapsed = section == "parents";
                sections[section] = head;
            }

            var row = _tree.CreateItem(head);
            row.SetText(0, RowLabel(t.Key, section));
            row.SetText(1, Ui.Pretty(t.ValueText));
            row.SetMetadata(0, t.Key);

            string? before = _baseline is null ? null : ValueOf(_baseline, t.Key);
            if (t.Source == RollSource.Override)
            {
                row.SetCustomColor(0, Ui.Override);
                row.SetCustomColor(1, Ui.Override);
                row.SetTooltipText(1, before is null ? "Overridden" : $"Overridden (rolled value: {Ui.Pretty(before)})");
            }
            else if (before is not null && before != t.ValueText)
            {
                row.SetCustomColor(1, Ui.Changed);
                row.SetTooltipText(1, $"Changed by an override (was {Ui.Pretty(before)})");
            }

            if (t.Key == _selectedKey) selected = row;
        }

        if (selected is not null)
        {
            selected.Select(0);
            _tree.ScrollToItem(selected);
        }
        _updating = false;
    }

    void ShowDetail()
    {
        _updating = true;
        var t = _selectedKey is null ? null : _pawn?.TraceFor(_selectedKey);
        if (t is null || Host.Data is null)
        {
            _detailTitle.Text = "Click a value on the left to see how it was rolled.";
            _overrideRow.Visible = false;
            _detail.Text = "";
            _updating = false;
            return;
        }

        _detailTitle.Text = t.Key;
        string text = TraceText.Describe(t);
        string? before = _baseline is null ? null : ValueOf(_baseline, t.Key);
        if (before is not null && before != t.ValueText)
            text += $"\nWithout overrides this was: {before}\n";
        _detail.Text = text;

        _overrideRow.Visible = true;
        bool canOverride = t.Source != RollSource.Derived;
        _valuePicker.Visible = _numberInput.Visible = _clearOne.Visible = false;
        if (canOverride)
        {
            var allowed = Host.Data.AllowedValues(t.Key);
            _valuePicker.Visible = allowed is not null;
            _numberInput.Visible = allowed is null;
            _clearOne.Visible = true;
            if (allowed is not null)
            {
                _valuePicker.Clear();
                foreach (var v in allowed) _valuePicker.AddItem(v);
                _valuePicker.Selected = allowed.ToList().IndexOf(t.Value!);
            }
            else
            {
                bool year = t.Key == "year";
                _numberInput.MinValue = year ? Host.Data.MinBirthYear : 0;
                _numberInput.MaxValue = year ? Host.Data.MaxBirthYear : 1;
                _numberInput.Step = year ? 1 : 0.01;
                _numberInput.Value = t.Number ?? 0;
            }
            _clearOne.Disabled = !_overrides.Contains(t.Key);
        }
        _updating = false;
    }

    void UpdateSummary()
    {
        if (_overrides.Count == 0)
        {
            SetStatus("No overrides. Pick a value and use Override to force it.");
            return;
        }
        int changed = _pawn!.Trace!.Count(t =>
            t.Source != RollSource.Override && ValueOf(_baseline!, t.Key) is string b && b != t.ValueText);
        var names = _overrides.Categorical.Select(kv => $"{kv.Key}={kv.Value}")
            .Concat(_overrides.Numeric.Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key}={kv.Value:0.##}")));
        SetStatus($"{_overrides.Count} override(s): {string.Join(", ", names)}. "
            + $"{changed} other value(s) changed as a result (blue).");
    }

    void SetStatus(string message, bool error = false)
    {
        _status.Text = message;
        Ui.SetColor(_status, error ? Ui.Error : null);
    }

    static string? ValueOf(Pawn p, string key) =>
        p.Facts.Cat(key) ?? p.Facts.Num(key)?.ToString("0.###", CultureInfo.InvariantCulture);

    static string RowLabel(string key, string section) => key switch
    {
        "year" => "birth year",
        _ when key.StartsWith("parents.same.") => Ui.Pretty(key["parents.same.".Length..]),
        _ => Ui.Pretty(key[(section.Length + 1)..]),
    };

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ------------------------------------------------------------ layout

    void BuildUi()
    {
        AddThemeConstantOverride("separation", 10);

        var bar = new HBoxContainer();
        AddChild(bar);
        bar.AddChild(new Label { Text = "Seed" });
        _seedInput = new LineEdit { CustomMinimumSize = new Vector2(200, 0) };
        _seedInput.TextSubmitted += _ => OnSeedEntered();
        bar.AddChild(_seedInput);
        bar.AddChild(Ui.Button("Go", OnSeedEntered));
        bar.AddChild(Ui.Button("<", () => SetSeed(_seed - 1), "Previous seed"));
        bar.AddChild(Ui.Button(">", () => SetSeed(_seed + 1), "Next seed"));
        bar.AddChild(Ui.Button("Random", () => SetSeed((ulong)Random.Shared.NextInt64())));
        bar.AddChild(new VSeparator());
        bar.AddChild(Ui.Button("Clear overrides", () => { _overrides.Clear(); Regenerate(); }));

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(split);

        _tree = new Tree
        {
            Columns = 2, HideRoot = true, ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(380, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _tree.SetColumnTitle(0, "Fact");
        _tree.SetColumnTitle(1, "Value");
        _tree.ItemSelected += () => { if (!_updating) OnTreeSelected(); };
        split.AddChild(_tree);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        split.AddChild(right);

        _detailTitle = Ui.Heading("", 20);
        right.AddChild(_detailTitle);

        _overrideRow = new HBoxContainer();
        right.AddChild(_overrideRow);
        _valuePicker = new OptionButton { CustomMinimumSize = new Vector2(220, 0), TooltipText = "Force this value" };
        _valuePicker.ItemSelected += index =>
        {
            if (!_updating && _selectedKey is not null)
                SetOverride(_selectedKey, _valuePicker.GetItemText((int)index), null);
        };
        _overrideRow.AddChild(_valuePicker);
        _numberInput = new SpinBox { CustomMinimumSize = new Vector2(140, 0), TooltipText = "Force this value" };
        _numberInput.ValueChanged += value =>
        {
            if (!_updating && _selectedKey is not null) SetOverride(_selectedKey, null, value);
        };
        _overrideRow.AddChild(_numberInput);
        _clearOne = Ui.Button("Clear override", () =>
        {
            if (_selectedKey is null) return;
            _overrides.Remove(_selectedKey);
            Regenerate();
        }, "Remove this override and roll normally");
        _overrideRow.AddChild(_clearOne);
        _overrideRow.AddChild(new VSeparator());
        _overrideRow.AddChild(Ui.Button("Edit this trait", () =>
        {
            if (_selectedKey is not null) EditTraitRequested?.Invoke(GameData.SchemaKey(_selectedKey));
        }, "Open this trait's definition in the Traits tab"));

        _detail = new RichTextLabel
        {
            BbcodeEnabled = false, SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _detail.AddThemeFontOverride("normal_font", Ui.Mono);
        right.AddChild(_detail);

        AddChild(Ui.Text("Orange: overridden.  Blue: changed because of an override.  Hover a value for details.", Ui.Dim));
        _status = Ui.Text("");
        AddChild(_status);
    }
}
