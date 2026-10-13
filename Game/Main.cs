using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using YouWillDate.Sim;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Game;

/// <summary>
/// Pawn inspector. Pick a seed, click any value to see how it was rolled,
/// override values to see what changes downstream. Data files reload when
/// saved, so you can edit the YAML and watch the pawn update.
/// </summary>
public partial class Main : Control
{
    static readonly Color OverrideColor = new(1f, 0.6f, 0.2f);
    static readonly Color ChangedColor = new(0.45f, 0.75f, 1f);
    static readonly Color ErrorColor = new(1f, 0.4f, 0.4f);

    static readonly Dictionary<string, string> SectionTitles = new()
    {
        ["pawn"] = "Pawn", ["context"] = "Context", ["mother"] = "Mother", ["father"] = "Father",
        ["parents"] = "Parents match?", ["household"] = "Household",
        ["surroundings"] = "Surroundings", ["climate"] = "Home climate",
    };

    string _dataDir = "";
    readonly Facts _overrides = new();
    PawnGenerator? _gen;
    DateTime _dataStamp;
    ulong _seed;
    Pawn? _pawn, _baseline;
    string? _selectedKey;
    bool _updating;

    LineEdit _seedInput = null!;
    CheckBox _autoReload = null!;
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
        _dataDir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "Data"));
        BuildUi();

        var timer = new Timer { WaitTime = 1.0, Autostart = true };
        timer.Timeout += () => { if (_autoReload.ButtonPressed) ReloadIfChanged(); };
        AddChild(timer);

        _seed = (ulong)Random.Shared.NextInt64();
        _seedInput.Text = _seed.ToString();
        ReloadIfChanged();
    }

    // ---------------------------------------------------------------- data

    void ReloadIfChanged(bool force = false)
    {
        DateTime stamp;
        try
        {
            stamp = Directory.GetFiles(_dataDir, "*.yaml").Max(File.GetLastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus($"Can't read the data folder {_dataDir}: {e.Message}", error: true);
            return;
        }
        if (!force && stamp == _dataStamp) return;
        // Remember the stamp even if loading fails, so a broken file reports
        // once instead of every second; the next save tries again.
        _dataStamp = stamp;

        GameData data;
        try { data = GameData.Load(_dataDir); }
        catch (Exception e) when (e is DataException or IOException)
        {
            GD.PushError($"Could not load data: {e.Message}");
            SetStatus($"Data error, keeping the previous data:\n{e.Message}", error: true);
            return;
        }

        _gen = new PawnGenerator(data);
        var dropped = DropInvalidOverrides(data);
        Regenerate();
        if (_pawn is not null)
            SetStatus($"Data loaded at {DateTime.Now:HH:mm:ss}."
                + (dropped.Count > 0 ? $" Dropped overrides that no longer fit: {string.Join(", ", dropped)}." : ""));
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
        if (_gen is null) return;
        try
        {
            _pawn = _gen.Generate(_seed, new GenerateOptions { Overrides = _overrides, Trace = true });
            _baseline = _overrides.Count > 0 ? _gen.Generate(_seed) : null;
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
            row.SetText(1, Pretty(t.ValueText));
            row.SetMetadata(0, t.Key);

            string? before = _baseline is null ? null : ValueOf(_baseline, t.Key);
            if (t.Source == RollSource.Override)
            {
                row.SetCustomColor(0, OverrideColor);
                row.SetCustomColor(1, OverrideColor);
                row.SetTooltipText(1, before is null ? "Overridden" : $"Overridden (rolled value: {Pretty(before)})");
            }
            else if (before is not null && before != t.ValueText)
            {
                row.SetCustomColor(1, ChangedColor);
                row.SetTooltipText(1, $"Changed by an override (was {Pretty(before)})");
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
        if (t is null || _gen is null)
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

        bool canOverride = t.Source != RollSource.Derived;
        _overrideRow.Visible = canOverride;
        if (canOverride)
        {
            var allowed = _gen.Data.AllowedValues(t.Key);
            _valuePicker.Visible = allowed is not null;
            _numberInput.Visible = allowed is null;
            if (allowed is not null)
            {
                _valuePicker.Clear();
                foreach (var v in allowed) _valuePicker.AddItem(v);
                _valuePicker.Selected = allowed.ToList().IndexOf(t.Value!);
            }
            else
            {
                bool year = t.Key == "year";
                _numberInput.MinValue = year ? _gen.Data.MinBirthYear : 0;
                _numberInput.MaxValue = year ? _gen.Data.MaxBirthYear : 1;
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
            SetStatus($"Data loaded from {_dataDir}. Auto-reload picks up saved changes.");
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
        if (error) _status.AddThemeColorOverride("font_color", ErrorColor);
        else _status.RemoveThemeColorOverride("font_color");
    }

    static string? ValueOf(Pawn p, string key) =>
        p.Facts.Cat(key) ?? p.Facts.Num(key)?.ToString("0.###", CultureInfo.InvariantCulture);

    static string RowLabel(string key, string section) => key switch
    {
        "year" => "birth year",
        _ when key.StartsWith("parents.same.") => Pretty(key["parents.same.".Length..]),
        _ => Pretty(key[(section.Length + 1)..]),
    };

    static string Pretty(string id) => id.Replace('_', ' ');
    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ------------------------------------------------------------ layout

    void BuildUi()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        // Toolbar
        var bar = new HBoxContainer();
        root.AddChild(bar);
        bar.AddChild(new Label { Text = "Seed" });
        _seedInput = new LineEdit { CustomMinimumSize = new Vector2(200, 0) };
        _seedInput.TextSubmitted += _ => OnSeedEntered();
        bar.AddChild(_seedInput);
        bar.AddChild(MakeButton("Go", OnSeedEntered));
        bar.AddChild(MakeButton("<", () => SetSeed(_seed - 1), "Previous seed"));
        bar.AddChild(MakeButton(">", () => SetSeed(_seed + 1), "Next seed"));
        bar.AddChild(MakeButton("Random", () => SetSeed((ulong)Random.Shared.NextInt64())));
        bar.AddChild(new VSeparator());
        bar.AddChild(MakeButton("Clear overrides", () => { _overrides.Clear(); Regenerate(); }));
        bar.AddChild(new VSeparator());
        bar.AddChild(MakeButton("Reload data", () => ReloadIfChanged(force: true)));
        _autoReload = new CheckBox { Text = "Auto-reload", ButtonPressed = true,
            TooltipText = "Reload the YAML files in Data/ whenever one is saved" };
        bar.AddChild(_autoReload);

        // Fact tree | detail
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(split);

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

        _detailTitle = new Label();
        _detailTitle.AddThemeFontSizeOverride("font_size", 20);
        right.AddChild(_detailTitle);

        _overrideRow = new HBoxContainer();
        right.AddChild(_overrideRow);
        _overrideRow.AddChild(new Label { Text = "Override:" });
        _valuePicker = new OptionButton { CustomMinimumSize = new Vector2(220, 0) };
        _valuePicker.ItemSelected += index =>
        {
            if (!_updating && _selectedKey is not null)
                SetOverride(_selectedKey, _valuePicker.GetItemText((int)index), null);
        };
        _overrideRow.AddChild(_valuePicker);
        _numberInput = new SpinBox { CustomMinimumSize = new Vector2(140, 0) };
        _numberInput.ValueChanged += value =>
        {
            if (!_updating && _selectedKey is not null) SetOverride(_selectedKey, null, value);
        };
        _overrideRow.AddChild(_numberInput);
        _clearOne = MakeButton("Clear", () =>
        {
            if (_selectedKey is null) return;
            _overrides.Remove(_selectedKey);
            Regenerate();
        }, "Remove this override and roll normally");
        _overrideRow.AddChild(_clearOne);

        _detail = new RichTextLabel
        {
            BbcodeEnabled = false, SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        var mono = new SystemFont { FontNames = ["Consolas", "Cascadia Mono", "Courier New", "monospace"] };
        _detail.AddThemeFontOverride("normal_font", mono);
        right.AddChild(_detail);

        // Footer
        root.AddChild(new Label
        {
            Text = "Orange: overridden.  Blue: changed because of an override.  Hover a value for details.",
            Modulate = new Color(1, 1, 1, 0.6f),
        });
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        root.AddChild(_status);
    }

    static Button MakeButton(string text, Action onPressed, string? tooltip = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "" };
        b.Pressed += onPressed;
        return b;
    }
}
