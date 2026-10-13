using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Godot;
using YouWillDate.Sim.Population;
using YouWillDate.Sim.Relations;

namespace YouWillDate.Game;

/// <summary>
/// A linked city: pick a resident to see the people they are tied to (current
/// partner, past partners, close friends), whether each is a resident or only a
/// record, and their starting-state relationship records. Click a resident in
/// the list on the right to jump to them.
/// </summary>
public partial class CityView : VBoxContainer
{
    public DataHost Host { get; init; } = null!;

    /// <summary>Raised with a pawn seed when the user asks to open a resident in the pawn inspector.</summary>
    public event Action<ulong>? OpenPawnRequested;

    City? _city;
    int? _selected;
    readonly Stack<int> _back = new();
    bool _busy, _updating;

    LineEdit _seedInput = null!;
    SpinBox _sizeInput = null!;
    CheckBox _linkedOnly = null!;
    Tree _residents = null!;
    Label _title = null!;
    Button _backButton = null!, _openButton = null!;
    Tree _people = null!;
    RichTextLabel _detail = null!;
    Label _status = null!;

    public override void _Ready()
    {
        BuildUi();
        Host.Reloaded += OnDataReloaded;
    }

    public override void _ExitTree() => Host.Reloaded -= OnDataReloaded;

    void OnDataReloaded()
    {
        // The old city was built from the old data; rebuild it with the same seed and size.
        if (_city is not null) Generate();
    }

    // ------------------------------------------------------------ actions

    async void Generate()
    {
        if (_busy) return;
        if (Host.Data is not { Relations: not null, Aura: not null } data)
        {
            SetStatus("A city needs relationships.yaml and aura.yaml in the Data folder.", error: true);
            return;
        }
        if (!ulong.TryParse(_seedInput.Text.Trim(), out ulong seed))
        {
            SetStatus("The world seed must be a whole number.", error: true);
            return;
        }
        int size = (int)_sizeInput.Value;
        _busy = true;
        SetStatus($"Generating a city of about {size} from world seed {seed}...");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _city = await Task.Run(() => new CityGenerator(data).Generate(seed, size));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            SetStatus(e.Message, error: true);
            return;
        }
        finally { _busy = false; }

        var c = _city;
        SetStatus(string.Create(CultureInfo.InvariantCulture,
            $"{c.Residents.Count} residents, {c.Couples.Count} couples, {c.Stats.ExLinks} ex links, {c.Stats.FriendLinks} friend links. " +
            $"Built in {watch.Elapsed.TotalSeconds:0.0} s."));
        _back.Clear();
        if (_selected is int s && s >= c.Residents.Count) _selected = null;
        FillResidents();
        ShowResident(_selected ?? c.Residents.FirstOrDefault(r => r.PartnerId is not null)?.Id);
    }

    void Select(int id, bool remember = true)
    {
        if (remember && _selected is int current && current != id) _back.Push(current);
        ShowResident(id);
        SelectInList(id);
    }

    // ---------------------------------------------------------------- view

    void FillResidents()
    {
        _updating = true;
        _residents.Clear();
        var root = _residents.CreateItem();
        foreach (var r in _city!.Residents)
        {
            int linked = r.PartnerLinks.Count(l => l is not null) + r.FriendLinks.Count(l => l is not null);
            if (_linkedOnly.ButtonPressed && linked == 0) continue;
            var row = _residents.CreateItem(root);
            row.SetText(0, $"#{r.Id}");
            row.SetText(1, Who(r));
            row.SetText(2, linked == 0 ? "" : linked.ToString(CultureInfo.InvariantCulture));
            row.SetTooltipText(2, "People in the city this resident is linked to");
            row.SetMetadata(0, r.Id);
        }
        _updating = false;
        if (_selected is int s) SelectInList(s);
    }

    void SelectInList(int id)
    {
        _updating = true;
        for (var row = _residents.GetRoot()?.GetFirstChild(); row is not null; row = row.GetNext())
            if (row.GetMetadata(0).AsInt32() == id)
            {
                row.Select(0);
                _residents.ScrollToItem(row);
                break;
            }
        _updating = false;
    }

    void ShowResident(int? id)
    {
        _selected = id;
        _backButton.Disabled = _back.Count == 0;
        _openButton.Disabled = id is null;
        _people.Clear();
        if (_city is null || id is not int rid)
        {
            _title.Text = _city is null ? "Generate a city to see who is linked to whom." : "Pick a resident on the left.";
            _detail.Text = "";
            return;
        }

        var c = _city;
        var r = c.Residents[rid];
        _title.Text = $"#{r.Id}  {Who(r)}";

        var root = _people.CreateItem();
        TreeItem Section(string title)
        {
            var head = _people.CreateItem(root);
            head.SetText(0, title);
            head.SetSelectable(0, false);
            head.SetSelectable(1, false);
            head.SetSelectable(2, false);
            return head;
        }
        void Person(TreeItem parent, string relation, int? resident, string details)
        {
            var row = _people.CreateItem(parent);
            row.SetText(0, relation);
            row.SetText(1, resident is int other ? $"#{other}  {Who(c.Residents[other])}" : "record only");
            row.SetText(2, details);
            row.SetTooltipText(2, details);
            if (resident is int o)
            {
                row.SetMetadata(0, o);
                row.SetCustomColor(1, Ui.Changed);
                row.SetTooltipText(1, "Click to go to this resident");
            }
            else
            {
                row.SetCustomColor(1, Ui.Dim);
                row.SetTooltipText(1, "Not a resident: a record with its own seed, which the game can promote to a pawn later");
            }
        }

        var partners = Section($"Partners ({r.Pawn.Partners.Count})");
        for (int i = 0; i < r.Pawn.Partners.Count; i++)
        {
            var p = r.Pawn.Partners[i];
            string span = $"ages {p.StartAge}–{(p.EndAge?.ToString(CultureInfo.InvariantCulture) ?? "now")}";
            string ended = p.EndReason is null ? "" : $", ended: {Ui.Pretty(p.EndReason)}";
            string details = string.Create(CultureInfo.InvariantCulture,
                $"{span}, {p.Sex}{(p.Married ? ", married" : "")}{ended}; standing {p.Status:+0.0;-0.0}, availability {p.Availability:0.00}");
            Person(partners, p.Ended ? "past partner" : $"current ({r.Relationship})", r.PartnerLinks[i], details);
        }
        if (r.Pawn.Partners.Count == 0) Person(partners, "none", null, "No relationships in the life history.");

        var friends = Section($"Close friends ({r.Friends.Count})");
        for (int i = 0; i < r.Friends.Count; i++)
        {
            var f = r.Friends[i];
            Person(friends, Ui.Pretty(f.Source), r.FriendLinks[i], string.Create(CultureInfo.InvariantCulture,
                $"{f.Sex}, known {f.YearsKnown} years, closeness {f.Closeness:0.00}, standing {f.StatusZ:+0.0;-0.0}"));
        }

        _detail.Text = StartingStateText(c, r);
    }

    static string StartingStateText(City c, Resident r)
    {
        var s = c.Start;
        var state = s.Pawns[r.Id];
        string N(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.AppendLine("Starting state (spec section 9)");
        sb.AppendLine($"  mood {N(state.Mood)}   loneliness {N(state.Loneliness)}   threat {N(state.Threat)}   " +
                      $"goal pressure {N(state.GoalPressure)}   readiness {N(s.Readiness(r.Id))}");
        if (state.MonthsSinceBreakup is double m)
            sb.AppendLine($"  last breakup {m:0} months ago");
        var grudges = state.Exes.Where(e => e.Grudge >= 0.01).ToList();
        foreach (var g in grudges)
            sb.AppendLine($"  grudge {N(g.Grudge)} toward past partner {g.PartnerIndex + 1}" +
                          (g.Resident is int other ? $" (#{other})" : "") + $", ended: {Ui.Pretty(g.EndReason ?? "?")}");

        foreach (var rec in s.Of(r.Id))
        {
            int other = rec.Other(r.Id);
            var mine = rec.Side(r.Id);
            var theirs = rec.Side(other);
            sb.AppendLine();
            sb.AppendLine($"With #{other} ({rec.Origin}{(rec.Dissolved ? ", dissolved" : "")}), since day {rec.StartedDay}");
            if (rec.Agreements.Count > 0)
                sb.AppendLine("  agreements: " + string.Join(", ", rec.Agreements.Select(a =>
                    string.Create(CultureInfo.InvariantCulture, $"{a.Op} (mass {a.Mass:0.#}, exclusivity {a.Exclusivity:0.00}, day {a.MadeDay})"))));
            sb.AppendLine("  readouts: " + string.Join(", ", c.World.Readouts.Keys.Select(id =>
                string.Create(CultureInfo.InvariantCulture, $"{Ui.Pretty(id)} {c.World.Readout(id, rec.Pattern):0.##}"))));
            sb.AppendLine($"  {"",-14}{"this pawn",10}{"#" + other,10}");
            void Row(string label, double a, double b) => sb.AppendLine($"  {label,-14}{N(a),10}{N(b),10}");
            Row("trust", mine.Trust, theirs.Trust);
            Row("investment", mine.Investment, theirs.Investment);
            Row("drive", mine.Drive, theirs.Drive);
            Row("hidden resent.", mine.HiddenResentment, theirs.HiddenResentment);
            Row("resentment", mine.Resentment, theirs.Resentment);
            Row("grudge", mine.Grudge, theirs.Grudge);
            Row("|D − P|", Dist(mine.Desire, rec.Pattern), Dist(theirs.Desire, rec.Pattern));
            int knows = state.Knowledge.Count(k => k.Subject == other);
            int known = s.Pawns[other].Knowledge.Count(k => k.Subject == r.Id);
            sb.AppendLine($"  knows {knows} facts about #{other}; #{other} knows {known} about this pawn");
        }
        return sb.ToString();
    }

    static string Who(Resident r)
    {
        string orientation = r.Pawn.Facts.Cat("identity.orientation") is { } o && o != "straight" ? $", {o}" : "";
        return $"{r.Sex}, {r.Age}, {r.Relationship}{orientation}";
    }

    static double Dist(double[] a, double[] b) =>
        a.Length == 0 ? 0 : Math.Sqrt(a.Select((x, i) => (x - b[i]) * (x - b[i])).Sum());

    void SetStatus(string message, bool error = false)
    {
        _status.Text = message;
        Ui.SetColor(_status, error ? Ui.Error : null);
    }

    // ------------------------------------------------------------ layout

    void BuildUi()
    {
        AddThemeConstantOverride("separation", 10);

        var bar = new HBoxContainer();
        AddChild(bar);
        bar.AddChild(new Label { Text = "World seed" });
        _seedInput = new LineEdit { Text = "1", CustomMinimumSize = new Vector2(160, 0) };
        _seedInput.TextSubmitted += _ => Generate();
        bar.AddChild(_seedInput);
        bar.AddChild(new Label { Text = "Residents" });
        _sizeInput = new SpinBox { MinValue = 20, MaxValue = 5000, Step = 10, Value = 300, TooltipText = "About this many residents" };
        bar.AddChild(_sizeInput);
        bar.AddChild(Ui.Button("Generate", Generate, "Build the city (the first one also calibrates, a few seconds)"));
        bar.AddChild(Ui.Button("Random", () =>
        {
            _seedInput.Text = ((ulong)Random.Shared.NextInt64()).ToString(CultureInfo.InvariantCulture);
            Generate();
        }));
        bar.AddChild(new VSeparator());
        _linkedOnly = new CheckBox { Text = "Only residents with links", ButtonPressed = true };
        _linkedOnly.Toggled += _ => { if (_city is not null) FillResidents(); };
        bar.AddChild(_linkedOnly);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(split);

        _residents = new Tree
        {
            Columns = 3, HideRoot = true, ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(360, 0), SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _residents.SetColumnTitle(0, "#");
        _residents.SetColumnTitle(1, "Resident");
        _residents.SetColumnTitle(2, "Links");
        _residents.SetColumnExpand(0, false);
        _residents.SetColumnCustomMinimumWidth(0, 60);
        _residents.SetColumnExpand(2, false);
        _residents.SetColumnCustomMinimumWidth(2, 50);
        _residents.ItemSelected += () =>
        {
            if (_updating || _residents.GetSelected() is not { } row) return;
            Select(row.GetMetadata(0).AsInt32());
        };
        split.AddChild(_residents);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 8);
        split.AddChild(right);

        var head = new HBoxContainer();
        right.AddChild(head);
        _backButton = Ui.Button("<", () => { if (_back.Count > 0) Select(_back.Pop(), remember: false); }, "Back to the previous resident");
        head.AddChild(_backButton);
        _title = Ui.Heading("Generate a city to see who is linked to whom.", 20);
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_title);
        _openButton = Ui.Button("Open in pawn inspector", () =>
        {
            if (_city is not null && _selected is int id) OpenPawnRequested?.Invoke(_city.Residents[id].Pawn.Seed);
        }, "Show this resident's full starting point, life and adult traits");
        head.AddChild(_openButton);

        var vsplit = new VSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(vsplit);
        _people = new Tree
        {
            Columns = 3, HideRoot = true, ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(0, 220), SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _people.SetColumnTitle(0, "Relation");
        _people.SetColumnTitle(1, "Who");
        _people.SetColumnTitle(2, "Details");
        _people.ItemSelected += () =>
        {
            var meta = _people.GetSelected()?.GetMetadata(0);
            if (meta is { VariantType: Variant.Type.Int } m) Select(m.AsInt32());
        };
        vsplit.AddChild(_people);

        _detail = new RichTextLabel
        {
            BbcodeEnabled = false, SelectionEnabled = true,
            SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 200),
        };
        _detail.AddThemeFontOverride("normal_font", Ui.Mono);
        vsplit.AddChild(_detail);

        AddChild(Ui.Text("Blue: a resident (click to go there).  Grey: a record only, with a seed for promoting it to a pawn later.", Ui.Dim));
        _status = Ui.Text("");
        AddChild(_status);
        ShowResident(null);
    }
}
