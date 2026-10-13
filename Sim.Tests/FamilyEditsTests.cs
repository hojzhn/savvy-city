using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Tests;

public class FamilyEditsTests
{
    static FamilyFile Family() => YamlIO.Clone(TestData.Load().FamilySource);

    static GameData Build(FamilyFile family, Config? config = null)
    {
        var data = TestData.Load();
        return GameData.FromFiles(config ?? YamlIO.Clone(data.Config), data.LocationsSource, data.ErasSource, family);
    }

    [Fact]
    public void RenamingAPerParentItemUpdatesEveryReference()
    {
        var f = Family();
        var (g, item) = FamilyEdits.FindItem(f, "parent.religion")!.Value;
        FamilyEdits.RenameItem(f, g, item, "faith");

        var data = Build(f); // still valid: all references followed the rename
        Assert.True(data.Schema.ContainsKey("parent.faith"));
        Assert.False(data.Schema.ContainsKey("parent.religion"));
        Assert.NotEmpty(FamilyEdits.ReferencesTo(f, "parent.faith"));
        Assert.Empty(FamilyEdits.ReferencesTo(f, "parent.religion"));
    }

    [Fact]
    public void RenamingAHouseholdItemAndGroupUpdatesReferences()
    {
        var f = Family();
        var (g, item) = FamilyEdits.FindItem(f, "household.wealth")!.Value;
        FamilyEdits.RenameItem(f, g, item, "money");
        FamilyEdits.RenameGroup(f, g, "home");

        var data = Build(f);
        Assert.True(data.Schema.ContainsKey("home.money"));
        Assert.Contains(FamilyEdits.ReferencesTo(f, "home.money"), r => r.StartsWith("surroundings.neighborhood_wealth"));
    }

    [Fact]
    public void RenamingAValueUpdatesWeightsModsAndConditionsElsewhere()
    {
        var f = Family();
        var (g, item) = FamilyEdits.FindItem(f, "parent.mental_health_severity")!.Value;
        FamilyEdits.RenameValue(f, g, item, "stable", "well");

        var data = Build(f);
        Assert.Contains("well", data.Schema["parent.mental_health_severity"]!);
        Assert.Equal(70, item.Weights!["well"]);
        // mental_health_type.only_if was "!stable"
        var type = FamilyEdits.FindItem(f, "parent.mental_health_type")!.Value.Item;
        Assert.Equal("!well", type.OnlyIf!["self.mental_health_severity"]);
        // marriage_quality mods list [moderate, severe]; unaffected values stay put
        Assert.NotNull(FamilyEdits.FindItem(f, "household.marriage_quality"));
    }

    [Fact]
    public void RenamingAValueThatLocationsUseIsCaught()
    {
        // Location religion shares still say "catholic", so the loader must object.
        var f = Family();
        var (g, item) = FamilyEdits.FindItem(f, "parent.religion")!.Value;
        FamilyEdits.RenameValue(f, g, item, "catholic", "roman_catholic");
        var e = Assert.Throws<DataException>(() => Build(f));
        Assert.Contains("catholic", e.Message);
    }

    [Fact]
    public void RemovingAValueCleansItsWeightsAndFactors()
    {
        var f = Family();
        var item = FamilyEdits.FindItem(f, "household.wealth")!.Value.Item;
        FamilyEdits.RemoveValue(item, "wealthy");
        Assert.DoesNotContain("wealthy", item.Values);
        Assert.False(item.Weights!.ContainsKey("wealthy"));
        Assert.All(item.Mods, m => Assert.False(m.Mul.ContainsKey("wealthy")));
    }

    [Fact]
    public void RenamingATemperamentTraitUpdatesCorrelationsAndConditions()
    {
        var f = Family();
        var config = YamlIO.Clone(TestData.Load().Config);
        FamilyEdits.RenameTemperamentTrait(config, f, "threat_sensitivity", "anxiety_proneness");

        var data = Build(f, config);
        Assert.True(data.Schema.ContainsKey("pawn.anxiety_proneness"));
        Assert.Contains(config.Temperament.Correlations, c => c.A == "anxiety_proneness");
        var pawn = new PawnGenerator(data).Generate(1);
        Assert.True(pawn.Temperament.ContainsKey("anxiety_proneness"));
    }

    [Fact]
    public void UniqueIdsAvoidCollisions()
    {
        var f = Family();
        Assert.Equal("brand_new", FamilyEdits.UniqueItemId(f, "brand_new"));
        Assert.Equal("wealth_2", FamilyEdits.UniqueItemId(f, "wealth"));
    }
}
