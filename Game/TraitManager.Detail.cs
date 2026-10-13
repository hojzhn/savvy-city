using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Game;

// The right-hand panel: info, distribution, and editors for the selected trait.
public partial class TraitManager
{
    VBoxContainer? _infoBox, _distBox;
    readonly List<(int Mod, Label Label)> _modRateLabels = new();
    readonly List<(string Value, Label Label)> _shareLabels = new();
    ItemDto? _editingItem;

    void ShowSelected()
    {
        Ui.ClearChildren(_detail);
        _infoBox = _distBox = null;
        _modRateLabels.Clear();
        _shareLabels.Clear();
        _editingItem = null;

        if (_selected is null)
        {
            _detail.AddChild(Ui.Text("Select a trait on the left to see how it is determined and edit it.", Ui.Dim));
            return;
        }

        if (_selected.StartsWith("group:"))
        {
            if (SelectedGroup() is { } g) BuildGroupEditor(g);
            return;
        }

        var item = SelectedItem();
        string title = item is { } it ? it.Item.Id : _catalog?.Get(_selected)?.Label ?? _selected;
        _detail.AddChild(Ui.Heading(Ui.Pretty(title), 22));
        _detail.AddChild(Ui.Text(_selected + (item is { Group.PerParent: true } ? "   (rolled separately for mother and father)" : ""), Ui.Dim));

        _infoBox = new VBoxContainer();
        _infoBox.AddThemeConstantOverride("separation", 6);
        _detail.AddChild(_infoBox);
        _distBox = new VBoxContainer();
        _detail.AddChild(_distBox);
        _detail.AddChild(new HSeparator());

        if (item is { } sel) BuildItemEditor(sel.Group, sel.Item);
        else if (SelectedTemperamentTrait() is { } t) BuildTemperamentEditor(t);
        else if (_selected == "year") BuildYearEditor();
        else if (_selected == "pawn.sex") BuildSexEditor();
        else if (_selected == "pawn.current_city") BuildCurrentCityEditor();
        else if (_selected == "pawn.birth_location") BuildOriginsEditor();
        else if (_catalog?.Get(_selected)?.Kind == TraitKind.Adult)
            _detail.AddChild(Ui.Text("Adult traits are edited in Data/adult.yaml. Save the file and this tab reloads it, " +
                                     "with the distribution above updating. In the pawn inspector, the Adult traits tab shows how each value was computed.", Ui.Dim));
        else if (_catalog?.Get(_selected)?.Kind == TraitKind.Life)
            _detail.AddChild(Ui.Text("Life simulation items are edited in Data/life.yaml. Save the file and this tab reloads it, " +
                                     "with the distribution above updating. The pawn inspector's Life story shows how each roll came out.", Ui.Dim));
        else if (_selected.StartsWith("context."))
            _detail.AddChild(Ui.Text("Birth context values are looked up, not rolled: edit them per location and period in Data/locations.yaml (economic eras in Data/eras.yaml).", Ui.Dim));

        RefreshInfo();
    }

    /// <summary>Refresh the parts that depend on validation and sampling, without touching the editor widgets.</summary>
    void RefreshInfo()
    {
        if (_infoBox is null || _distBox is null || _selected is null) return;
        Ui.ClearChildren(_infoBox);
        Ui.ClearChildren(_distBox);

        var info = _catalog?.Get(_selected);
        if (info is null)
        {
            _infoBox.AddChild(Ui.Text("Details appear once the data is valid again.", Ui.Dim));
        }
        else
        {
            if (_draftData is null && _dirty)
                _infoBox.AddChild(Ui.Text("(Showing the last valid version while the draft has an error.)", Ui.Dim));
            _infoBox.AddChild(Ui.Heading("How it's determined"));
            _infoBox.AddChild(Ui.Text(info.HowDetermined));
            if (info.Note is not null) _infoBox.AddChild(Ui.Text("Note: " + info.Note, Ui.Dim));
            _infoBox.AddChild(Links("Depends on:", info.Reads, "nothing; base weights only"));
            _infoBox.AddChild(Links("Used by:", info.ReadBy, "nothing reads it yet"));
            BuildDistribution(info);
        }
        UpdateModRates();
    }

    Control Links(string label, IReadOnlyList<string> keys, string none)
    {
        var flow = new HFlowContainer();
        flow.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(100, 0) });
        if (keys.Count == 0) flow.AddChild(Ui.Text(none, Ui.Dim));
        foreach (var key in keys)
        {
            var link = new LinkButton { Text = key, TooltipText = _catalog?.Get(key)?.HowDetermined ?? "" };
            link.Pressed += () => SelectTrait(key);
            flow.AddChild(link);
        }
        return flow;
    }

    // ------------------------------------------------------- distribution

    void BuildDistribution(TraitInfo info)
    {
        var box = _distBox!;
        var stats = _dirty ? _draftStats : _savedStats;
        var compare = _dirty ? _savedStats : null;

        box.AddChild(Ui.Heading($"Across {SampleSize} sample pawns"));
        if (_dirty && compare is not null)
            box.AddChild(Ui.Text("With your unsaved edits. Numbers in brackets show the change from the saved version.", Ui.Dim));
        if (_statsError is not null) box.AddChild(Ui.Text("Sampling failed: " + _statsError, Ui.Error));
        if (stats is null)
        {
            box.AddChild(Ui.Text(_draftData is null && _dirty ? "Fix the error to see the distribution." : "Sampling...", Ui.Dim));
            return;
        }

        foreach (var factKey in info.FactKeys)
        {
            if (info.FactKeys.Count > 1) box.AddChild(Ui.Text(factKey[..factKey.IndexOf('.')], Ui.Dim));
            if (!stats.TryGetValue(factKey, out var s))
            {
                box.AddChild(Ui.Text("No samples yet.", Ui.Dim));
                continue;
            }
            var before = compare?.GetValueOrDefault(factKey);

            if (info.Values is not null)
            {
                foreach (var v in info.Values)
                    box.AddChild(BarRow(v, s.Share(v), before?.Share(v)));
            }
            else
            {
                foreach (var (label, share, prev) in Histogram(info.Key, s, before))
                    box.AddChild(BarRow(label, share, prev));
            }
        }
    }

    IEnumerable<(string Label, double Share, double? Before)> Histogram(string key, FactStats s, FactStats? before)
    {
        bool year = key == "year";
        bool percentile = key.StartsWith("pawn.");
        double lo, hi;
        int bins;
        if (year)
        {
            lo = _draftData?.MinBirthYear ?? Host.Data!.MinBirthYear;
            hi = (_draftData?.MaxBirthYear ?? Host.Data!.MaxBirthYear) + 1;
            bins = (int)Math.Ceiling((hi - lo) / 5);
        }
        else if (percentile)
        {
            (lo, hi, bins) = (0, 1, 10);
        }
        else
        {
            var all = s.Numbers.Concat(before?.Numbers ?? []).ToList();
            bool integers = all.All(v => v == Math.Floor(v));
            if (integers)
            {
                // Counts and ages: whole-number bins over the observed range, at most 12.
                lo = all.Count == 0 ? 0 : Math.Floor(all.Min());
                hi = all.Count == 0 ? 1 : Math.Floor(all.Max()) + 1;
                bins = (int)Math.Min(12, Math.Max(1, hi - lo));
            }
            else
            {
                // Scores: 12 even bins over the observed range.
                lo = Math.Floor(all.Min() * 10) / 10;
                hi = Math.Ceiling(all.Max() * 10) / 10;
                if (hi <= lo) hi = lo + 1;
                bins = 12;
                percentile = true;
            }
        }
        double width = (hi - lo) / bins;
        bool whole = !percentile;

        double[] Count(FactStats f)
        {
            var c = new double[bins];
            foreach (var n in f.Numbers)
                c[Math.Clamp((int)((n - lo) / width), 0, bins - 1)]++;
            return c.Select(x => f.Numbers.Count == 0 ? 0 : x / f.Numbers.Count).ToArray();
        }

        var now = Count(s);
        var prev = before is null ? null : Count(before);
        for (int i = 0; i < bins; i++)
        {
            double a = lo + i * width, b = a + width;
            string label = !whole ? string.Create(CultureInfo.InvariantCulture, $"{a:0.0}–{b:0.0}")
                : Math.Ceiling(b) - 1 <= Math.Ceiling(a) ? $"{Math.Ceiling(a):0}"
                : $"{Math.Ceiling(a):0}–{Math.Ceiling(b) - 1:0}";
            yield return (label, now[i], prev?[i]);
        }
    }

    static Control BarRow(string label, double share, double? before)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label
        {
            Text = Ui.Pretty(label), CustomMinimumSize = new Vector2(190, 0),
            ClipText = true, TooltipText = label,
        });
        row.AddChild(new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = share * 100, ShowPercentage = false,
            CustomMinimumSize = new Vector2(240, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter,
        });
        row.AddChild(new Label
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"{share * 100,5:0.0}%"),
            CustomMinimumSize = new Vector2(60, 0), HorizontalAlignment = HorizontalAlignment.Right,
        });
        if (before is double b && Math.Abs(share - b) >= 0.005)
        {
            var delta = new Label { Text = string.Create(CultureInfo.InvariantCulture, $"({(share - b) * 100:+0.0;-0.0})") };
            delta.AddThemeColorOverride("font_color", Ui.Changed);
            row.AddChild(delta);
        }
        return row;
    }

    void UpdateModRates()
    {
        if (_selected is null || _modRateLabels.Count == 0) return;
        var info = _catalog?.Get(_selected);
        var stats = _dirty ? _draftStats : _savedStats;
        foreach (var (mod, label) in _modRateLabels)
        {
            if (info is null || stats is null) { label.Text = ""; continue; }
            var parts = new List<string>();
            foreach (var factKey in info.FactKeys)
            {
                if (!stats.TryGetValue(factKey, out var s) || s.Rolled == 0 || mod >= s.ModFired.Length) continue;
                string pct = string.Create(CultureInfo.InvariantCulture, $"{100.0 * s.ModFired[mod] / s.Rolled:0.#}%");
                parts.Add(info.FactKeys.Count > 1 ? $"{factKey[..factKey.IndexOf('.')]} {pct}" : pct);
            }
            label.Text = parts.Count == 0 ? "" : "fires for " + string.Join(", ", parts) + " of rolls";
        }
    }

    // ----------------------------------------------------------- editors

    void BuildGroupEditor(GroupDto group)
    {
        _detail.AddChild(Ui.Heading("Group: " + Ui.Pretty(group.Name), 22));
        _detail.AddChild(Ui.Text(group.PerParent
            ? "Each trait in this group is rolled twice, once for the mother and once for the father. Rules read them as self.<id> inside the group and mother./father./any_parent.<id> elsewhere."
            : $"Traits in this group are rolled once and read as {group.Name}.<id>.", Ui.Dim));
        _detail.AddChild(Ui.Text("Groups roll in order, top to bottom, and a trait can only read traits above it.", Ui.Dim));

        var grid = Form();
        FormRow(grid, "Name", Field(group.Name, text =>
        {
            if (!IdPattern.IsMatch(text)) return "Use letters, digits and _ only.";
            if (_family.Groups.Any(g => g != group && g.Name == text)) return "Another group has that name.";
            FamilyEdits.RenameGroup(_family, group, text, _life, _adult);
            Changed(structural: true);
            return null;
        }));
        FormRow(grid, "Note", Field(group.Note ?? "", text =>
        {
            group.Note = text.Length == 0 ? null : text;
            Changed(structural: false, rules: false);
            return null;
        }, "What this group is for"));
        _detail.AddChild(Ui.Text($"{group.Items.Count} trait(s). Select one on the left, or use + Trait to add one.", Ui.Dim));
    }

    void BuildItemEditor(GroupDto group, ItemDto item)
    {
        _editingItem = item;
        _detail.AddChild(Ui.Heading("Definition", 18));

        var grid = Form();
        FormRow(grid, "Id", Field(item.Id, text =>
        {
            if (!IdPattern.IsMatch(text)) return "Use letters, digits and _ only.";
            if (group.Items.Any(i => i != item && i.Id == text)) return "Another trait in this group has that id.";
            FamilyEdits.RenameItem(_family, group, item, text, _life, _adult);
            _selected = FamilyEdits.ItemKey(group, item);
            Changed(structural: true);
            return null;
        }), "Renaming also updates every rule that reads this trait.");
        FormRow(grid, "Note", Field(item.Note ?? "", text =>
        {
            item.Note = text.Length == 0 ? null : text;
            Changed(structural: false, rules: false);
            return null;
        }, "Why this trait exists, sources for its numbers..."));

        var baseFrom = new OptionButton();
        string[] baseOptions = ["", "groups", "religions"];
        baseFrom.AddItem("The weights below");
        baseFrom.AddItem("Location ethnic shares (groups)");
        baseFrom.AddItem("Location religious shares (religions)");
        baseFrom.Selected = Math.Max(0, Array.IndexOf(baseOptions, item.BaseFrom ?? ""));
        baseFrom.ItemSelected += i =>
        {
            item.BaseFrom = baseOptions[i].Length == 0 ? null : baseOptions[i];
            if (item.BaseFrom is not null) item.Weights = null;
            Changed(structural: true);
        };
        FormRow(grid, "Base weights from", baseFrom);

        if (group.PerParent)
            FormRow(grid, "Father copies mother", Spin(item.Assort, 0, 1, 0.05, v =>
            {
                item.Assort = v;
                Changed(structural: false);
            }), "Chance the father simply takes the mother's value (couples tend to match). 0 = independent.");

        // Values and base weights
        _detail.AddChild(Ui.Heading("Values", 18));
        var values = new GridContainer { Columns = 4 };
        values.AddThemeConstantOverride("h_separation", 10);
        _detail.AddChild(values);
        values.AddChild(Ui.Text("Value", Ui.Dim));
        values.AddChild(Ui.Text(item.BaseFrom is null ? "Base weight" : "", Ui.Dim));
        values.AddChild(Ui.Text(item.BaseFrom is null ? "Base share" : "", Ui.Dim));
        values.AddChild(new Control());

        foreach (var value in item.Values.ToList())
        {
            var valueField = Field(value, text =>
            {
                if (!IdPattern.IsMatch(text)) return "Use letters, digits and _ only.";
                if (item.Values.Contains(text)) return "That value already exists.";
                FamilyEdits.RenameValue(_family, group, item, value, text, _life, _adult);
                Changed(structural: true);
                return null;
            });
            valueField.CustomMinimumSize = new Vector2(200, 0);
            values.AddChild(valueField);

            if (item.BaseFrom is null)
            {
                values.AddChild(Spin(item.Weights?.GetValueOrDefault(value, 1) ?? 1, 0, 1_000_000, 0.01, v =>
                {
                    item.Weights ??= new();
                    item.Weights[value] = v;
                    UpdateShareLabels();
                    Changed(structural: false);
                }));
                var share = new Label();
                _shareLabels.Add((value, share));
                values.AddChild(share);
            }
            else
            {
                values.AddChild(Ui.Text("from location", Ui.Dim));
                values.AddChild(new Control());
            }

            values.AddChild(Ui.Button("x", () =>
            {
                if (item.Values.Count <= 1) { SetBanner("A trait needs at least one value.", Ui.Override); return; }
                FamilyEdits.RemoveValue(item, value);
                Changed(structural: true);
            }, $"Remove '{value}' (and its weight and modifier factors)"));
        }
        UpdateShareLabels();
        _detail.AddChild(Ui.Button("+ Add value", () =>
        {
            string v = "new_value";
            for (int n = 2; item.Values.Contains(v); n++) v = $"new_value_{n}";
            item.Values.Add(v);
            Changed(structural: true);
        }));

        // only_if / default
        _detail.AddChild(Ui.Heading("Only roll when", 18));
        _detail.AddChild(Ui.Text("Leave empty to always roll. Otherwise the trait takes the fallback value when the conditions don't hold.", Ui.Dim));
        var onlyIf = Form();
        FormRow(onlyIf, "Conditions", Field(YamlIO.FormatInlineMap(item.OnlyIf), text =>
        {
            var parsed = YamlIO.ParseInlineMap<object>(text);
            item.OnlyIf = parsed.Count == 0 ? null : parsed;
            if (item.OnlyIf is null) item.Default = null;
            else if (item.Default is null || !item.Values.Contains(item.Default)) item.Default = item.Values[0];
            Changed(structural: true);
            return null;
        }, "e.g.  self.religion: \"!none\""));
        if (item.OnlyIf is not null)
        {
            var fallback = new OptionButton();
            foreach (var v in item.Values) fallback.AddItem(v);
            fallback.Selected = Math.Max(0, item.Values.IndexOf(item.Default ?? ""));
            fallback.ItemSelected += i =>
            {
                item.Default = item.Values[(int)i];
                Changed(structural: false);
            };
            FormRow(onlyIf, "Otherwise", fallback);
        }

        // Modifiers
        _detail.AddChild(Ui.Heading("Modifiers", 18));
        _detail.AddChild(Ui.Text(
            "Each modifier multiplies the listed weights when ALL its conditions hold.\n" +
            "Conditions: key: value   key: [a, b]   key: \"!a\" (not a)   key: \">=0.7\"   key: \"1990..1999\"\n" +
            "Keys: self.<trait> (this parent), mother./father./any_parent.<trait>, household.<trait>, context.<field>, pawn.<temperament>, year.", Ui.Dim));

        for (int m = 0; m < item.Mods.Count; m++)
            _detail.AddChild(ModEditor(item, m));

        _detail.AddChild(Ui.Button("+ Add modifier", () =>
        {
            item.Mods.Add(new ModDto
            {
                When = new() { ["context.urbanicity"] = "rural" },
                Mul = new() { [item.Values[0]] = 1.0 },
            });
            Changed(structural: true);
        }));
    }

    Control ModEditor(ItemDto item, int index)
    {
        var mod = item.Mods[index];
        var panel = new PanelContainer();
        var box = new VBoxContainer();
        panel.AddChild(Ui.Pad(box, 8));

        var head = new HBoxContainer();
        box.AddChild(head);
        head.AddChild(Ui.Heading($"Modifier {index + 1}", 15));
        var rate = Ui.Text("", Ui.Dim);
        rate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(rate);
        _modRateLabels.Add((index, rate));
        head.AddChild(Ui.Button("Up", () => SwapMods(item, index, index - 1)));
        head.AddChild(Ui.Button("Down", () => SwapMods(item, index, index + 1)));
        head.AddChild(Ui.Button("x", () =>
        {
            item.Mods.RemoveAt(index);
            Changed(structural: true);
        }, "Delete this modifier"));

        var grid = Form(box);
        FormRow(grid, "When", Field(YamlIO.FormatInlineMap(mod.When), text =>
        {
            var parsed = YamlIO.ParseInlineMap<object>(text);
            if (parsed.Count == 0) return "A modifier needs at least one condition.";
            mod.When = parsed;
            Changed(structural: false);
            return null;
        }, "e.g.  any_parent.religion: [evangelical, latter_day_saint]"));
        FormRow(grid, "Multiply", Field(YamlIO.FormatInlineMap(mod.Mul), text =>
        {
            mod.Mul = YamlIO.ParseInlineMap<double>(text);
            Changed(structural: false);
            return null;
        }, $"e.g.  {item.Values[0]}: 2"));
        FormRow(grid, "Note", Field(mod.Note ?? "", text =>
        {
            mod.Note = text.Length == 0 ? null : text;
            Changed(structural: false, rules: false);
            return null;
        }, "Why this modifier exists"));
        return panel;
    }

    void SwapMods(ItemDto item, int a, int b)
    {
        if (b < 0 || b >= item.Mods.Count) return;
        (item.Mods[a], item.Mods[b]) = (item.Mods[b], item.Mods[a]);
        Changed(structural: true);
    }

    void UpdateShareLabels()
    {
        if (_editingItem is not { } item) return;
        double total = item.Values.Sum(v => item.Weights?.GetValueOrDefault(v, 1) ?? 1);
        foreach (var (value, label) in _shareLabels)
        {
            double w = item.Weights?.GetValueOrDefault(value, 1) ?? 1;
            label.Text = total > 0 ? string.Create(CultureInfo.InvariantCulture, $"{100 * w / total:0.0}%") : "-";
        }
    }

    void BuildTemperamentEditor(string trait)
    {
        _detail.AddChild(Ui.Heading("Definition", 18));
        var grid = Form();
        FormRow(grid, "Name", Field(trait, text =>
        {
            if (!IdPattern.IsMatch(text)) return "Use letters, digits and _ only.";
            if (_config.Temperament.Traits.Contains(text)) return "That trait already exists.";
            FamilyEdits.RenameTemperamentTrait(_config, _family, trait, text, _life, _adult);
            _selected = $"pawn.{text}";
            Changed(structural: true);
            return null;
        }), "Renaming also updates correlations and every rule that reads pawn.<name>.");

        _detail.AddChild(Ui.Heading("Correlations", 18));
        _detail.AddChild(Ui.Text("How strongly this trait moves with the others (-1 to 1). Keep them mild; strong combinations can be impossible and are rejected.", Ui.Dim));

        var others = _config.Temperament.Traits.Where(t => t != trait).ToList();
        foreach (var c in _config.Temperament.Correlations.Where(c => c.A == trait || c.B == trait).ToList())
        {
            var row = new HBoxContainer();
            _detail.AddChild(row);
            row.AddChild(new Label { Text = "with" });
            var other = new OptionButton { CustomMinimumSize = new Vector2(200, 0) };
            foreach (var o in others) other.AddItem(o);
            other.Selected = others.IndexOf(c.A == trait ? c.B : c.A);
            other.ItemSelected += i =>
            {
                c.A = trait;
                c.B = others[(int)i];
                Changed(structural: true);
            };
            row.AddChild(other);
            row.AddChild(Spin(c.R, -0.95, 0.95, 0.05, v =>
            {
                c.R = v;
                Changed(structural: false);
            }));
            row.AddChild(Ui.Button("x", () =>
            {
                _config.Temperament.Correlations.Remove(c);
                Changed(structural: true);
            }, "Remove this correlation"));
        }

        var free = others.Where(o => !_config.Temperament.Correlations.Any(c =>
            (c.A == trait && c.B == o) || (c.B == trait && c.A == o))).ToList();
        if (free.Count > 0)
            _detail.AddChild(Ui.Button("+ Add correlation", () =>
            {
                _config.Temperament.Correlations.Add(new Correlation { A = trait, B = free[0], R = 0.1 });
                Changed(structural: true);
            }));
    }

    void BuildCurrentCityEditor()
    {
        _detail.AddChild(Ui.Heading("Settings (config.yaml)", 18));
        var ids = _locations.Locations.Select(l => l.Id).ToList();
        var picker = new OptionButton();
        foreach (var l in _locations.Locations) picker.AddItem(l.Name);
        picker.Selected = Math.Max(0, ids.IndexOf(_config.CurrentCity));
        picker.ItemSelected += i =>
        {
            _config.CurrentCity = ids[(int)i];
            Changed(structural: true);
        };
        FormRow(Form(), "The game takes place in", picker,
            "Every pawn lives here. Where they were born comes from this city's origins (see birth location).");
    }

    void BuildOriginsEditor()
    {
        var city = _locations.Locations.FirstOrDefault(l => l.Id == _config.CurrentCity);
        if (city is null)
        {
            _detail.AddChild(Ui.Text("Pick a valid current city first.", Ui.Error));
            return;
        }

        _detail.AddChild(Ui.Heading($"Where people living in {city.Name} were born", 18));
        if (city.Origins is null)
        {
            _detail.AddChild(Ui.Text($"{city.Name} has no origins table, so birth locations are weighted by each location's population.", Ui.Dim));
            _detail.AddChild(Ui.Button("Create an origins table", () =>
            {
                city.Origins = _locations.Locations.ToDictionary(l => l.Id, l => l.Population);
                Changed(structural: true);
            }, "Start from population weights, then adjust"));
            return;
        }

        _detail.AddChild(Ui.Text("Relative weights (locations.yaml, origins of the current city). Set 0 to never be born there.", Ui.Dim));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        _detail.AddChild(grid);
        grid.AddChild(Ui.Text("Born in", Ui.Dim));
        grid.AddChild(Ui.Text("Weight", Ui.Dim));
        grid.AddChild(Ui.Text("Share", Ui.Dim));

        var shares = new List<(string Id, Label Label)>();
        void UpdateShares()
        {
            double total = city.Origins!.Values.Sum();
            foreach (var (id, label) in shares)
                label.Text = total > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{100 * city.Origins.GetValueOrDefault(id) / total:0.0}%")
                    : "-";
        }

        foreach (var loc in _locations.Locations)
        {
            grid.AddChild(new Label { Text = loc.Name + (loc.Id == city.Id ? "  (born locally)" : "") });
            grid.AddChild(Spin(city.Origins.GetValueOrDefault(loc.Id), 0, 1_000_000, 0.1, v =>
            {
                city.Origins![loc.Id] = v;
                UpdateShares();
                Changed(structural: false);
            }));
            var share = new Label();
            shares.Add((loc.Id, share));
            grid.AddChild(share);
        }
        UpdateShares();

        _detail.AddChild(Ui.Button("Remove origins table", () =>
        {
            city.Origins = null;
            Changed(structural: true);
        }, "Go back to weighting birth locations by population"));
        _detail.AddChild(Ui.Text("New birth locations (with their own politics, norms and religion shares by period) are added in Data/locations.yaml.", Ui.Dim));
    }

    void BuildYearEditor()
    {
        _detail.AddChild(Ui.Heading("Settings (config.yaml)", 18));
        var grid = Form();
        FormRow(grid, "Present year", Spin(_config.PresentYear, 1900, 2300, 1, v => { _config.PresentYear = (int)v; Changed(structural: false); }));
        FormRow(grid, "Youngest age", Spin(_config.AgeMin, 1, 120, 1, v => { _config.AgeMin = (int)v; Changed(structural: false); }));
        FormRow(grid, "Oldest age", Spin(_config.AgeMax, 1, 120, 1, v => { _config.AgeMax = (int)v; Changed(structural: false); }));
    }

    void BuildSexEditor()
    {
        _detail.AddChild(Ui.Heading("Settings (config.yaml)", 18));
        var grid = Form();
        FormRow(grid, "Male birth rate", Spin(_config.MaleBirthRate, 0.01, 0.99, 0.001, v =>
        {
            _config.MaleBirthRate = v;
            Changed(structural: false);
        }));
    }

    // ----------------------------------------------------------- widgets

    GridContainer Form(Container? parent = null)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        (parent ?? _detail).AddChild(grid);
        return grid;
    }

    static void FormRow(GridContainer grid, string label, Control field, string? help = null)
    {
        grid.AddChild(new Label { Text = label, TooltipText = help ?? "" });
        if (help is null)
        {
            grid.AddChild(field);
            return;
        }
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(field);
        box.AddChild(Ui.Text(help, Ui.Dim));
        grid.AddChild(box);
    }

    /// <summary>
    /// A text field that commits on Enter or when it loses focus. The commit
    /// returns an error message to show (and keeps the text for fixing), or null.
    /// Parse errors from the YAML reader are shown the same way.
    /// </summary>
    LineEdit Field(string text, Func<string, string?> commit, string? placeholder = null)
    {
        var edit = new LineEdit
        {
            Text = text, PlaceholderText = placeholder ?? "",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 0),
        };
        string committed = text;
        void Commit()
        {
            string value = edit.Text.Trim();
            if (value == committed) return;
            // Mark as committed first: a successful commit can rebuild the panel,
            // and removing this field fires FocusExited again.
            string previous = committed;
            committed = value;
            string? error;
            try { error = commit(value); }
            catch (DataException e) { error = e.Message; }
            if (error is null)
            {
                edit.RemoveThemeColorOverride("font_color");
                edit.TooltipText = "";
            }
            else
            {
                committed = previous;
                edit.AddThemeColorOverride("font_color", Ui.Error);
                edit.TooltipText = error;
                SetBanner(error, Ui.Error);
            }
        }
        edit.TextSubmitted += _ => Commit();
        edit.FocusExited += Commit;
        return edit;
    }

    static SpinBox Spin(double value, double min, double max, double step, Action<double> onChange)
    {
        var spin = new SpinBox
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(120, 0),
        };
        // Connected after setting Value so building the widget doesn't count as an edit.
        spin.ValueChanged += v => onChange(v);
        return spin;
    }
}
