using YouWillDate.Sim.Data;

namespace YouWillDate.Sim.Tests;

static class TestData
{
    static readonly Lazy<GameData> Real = new(() => GameData.Load(DataDir));

    /// <summary>The game's real data tables.</summary>
    public static GameData Load() => Real.Value;

    public static string DataDir
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Data");
                if (File.Exists(Path.Combine(candidate, "config.yaml"))) return candidate;
            }
            throw new DirectoryNotFoundException("Data folder not found");
        }
    }

    public static string ReadFile(string name) => File.ReadAllText(Path.Combine(DataDir, name));

    /// <summary>Real config, locations, and eras with a custom family table.</summary>
    public static GameData WithFamily(string familyYaml) =>
        GameData.FromYaml(ReadFile("config.yaml"), ReadFile("locations.yaml"), ReadFile("eras.yaml"), familyYaml);
}
