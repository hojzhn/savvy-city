using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Sim.Tests;

public class CurrentCityTests
{
    static readonly GameData Data = TestData.Load();
    static readonly PawnGenerator Gen = new(Data);

    [Fact]
    public void EveryPawnLivesInTheGameCity()
    {
        for (ulong seed = 0; seed < 500; seed++)
        {
            var p = Gen.Generate(seed);
            Assert.Equal(Data.Config.CurrentCity, p.CurrentCity.Id);
            Assert.Equal(p.CurrentCity.Id, p.Facts["pawn.current_city"]);
            Assert.Equal(p.BirthLocation.Id == p.CurrentCity.Id ? "yes" : "no", p.Facts["pawn.born_locally"]);
        }
    }

    [Fact]
    public void BirthLocationsFollowTheCityOrigins()
    {
        var origins = Data.CurrentCity.Origins!;
        double total = origins.Values.Sum();
        int n = 20000;
        var counts = new Dictionary<string, int>();
        for (ulong seed = 0; seed < (ulong)n; seed++)
        {
            var id = Gen.Generate(seed).BirthLocation.Id;
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        foreach (var (id, weight) in origins)
            Assert.InRange(counts.GetValueOrDefault(id) / (double)n, weight / total - 0.015, weight / total + 0.015);
    }

    [Fact]
    public void BirthContextComesFromTheBirthLocation()
    {
        var p = Gen.Generate(3, new GenerateOptions { Overrides = Overrides("pawn.birth_location", "rural_ohio") });
        Assert.Equal("rural", p.Facts["context.urbanicity"]);
        Assert.Equal("no", p.Facts["pawn.born_locally"]);
        Assert.Equal("manhattan", p.CurrentCity.Id);
    }

    [Fact]
    public void CityWithoutOriginsFallsBackToPopulation()
    {
        var weights = Data.BirthLocationWeights(Data.LocationById("rural_ohio"));
        Assert.Equal(Data.Locations.Select(l => l.Population), weights);
    }

    [Fact]
    public void BadCurrentCityOrOriginsAreRejected()
    {
        var config = TestData.ReadFile("config.yaml").Replace("current_city: manhattan", "current_city: atlantis");
        var e = Assert.Throws<DataException>(() => GameData.FromYaml(config,
            TestData.ReadFile("locations.yaml"), TestData.ReadFile("eras.yaml"), TestData.ReadFile("family.yaml")));
        Assert.Contains("current_city 'atlantis'", e.Message);

        var locations = TestData.ReadFile("locations.yaml").Replace("origins: { manhattan: 22", "origins: { gotham: 22");
        e = Assert.Throws<DataException>(() => GameData.FromYaml(TestData.ReadFile("config.yaml"),
            locations, TestData.ReadFile("eras.yaml"), TestData.ReadFile("family.yaml")));
        Assert.Contains("'gotham'", e.Message);
    }

    [Fact]
    public void BornLocallyCannotBeOverridden()
    {
        var e = Assert.Throws<ArgumentException>(() =>
            Gen.Generate(1, new GenerateOptions { Overrides = Overrides("pawn.born_locally", "yes") }));
        Assert.Contains("derived", e.Message);
    }

    static Facts Overrides(string key, string value)
    {
        var f = new Facts();
        f.Set(key, value);
        return f;
    }
}
