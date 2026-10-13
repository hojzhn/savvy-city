using System;
using System.IO;
using System.Linq;
using Godot;
using YouWillDate.Sim.Data;
using YouWillDate.Sim.Generation;

namespace YouWillDate.Game;

/// <summary>
/// Owns the loaded data for every screen. Polls the Data folder and reloads
/// when a YAML file is saved; a broken file is reported and the last good
/// data stays in use.
/// </summary>
public partial class DataHost : Node
{
    public string DataDir { get; private set; } = "";
    public GameData? Data { get; private set; }
    public PawnGenerator? Generator { get; private set; }
    public TraitCatalog? Catalog { get; private set; }
    public bool AutoReload { get; set; } = true;

    /// <summary>Raised after new data loads successfully.</summary>
    public event Action? Reloaded;
    /// <summary>Load status for the status bar: message and whether it is an error.</summary>
    public event Action<string, bool>? StatusChanged;

    DateTime _stamp;

    public override void _Ready()
    {
        DataDir = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "Data"));
        var timer = new Timer { WaitTime = 1.0, Autostart = true };
        timer.Timeout += () => { if (AutoReload) ReloadIfChanged(); };
        AddChild(timer);
        ReloadIfChanged(force: true);
    }

    public void ReloadIfChanged(bool force = false)
    {
        DateTime stamp;
        try
        {
            stamp = Directory.GetFiles(DataDir, "*.yaml").Max(File.GetLastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusChanged?.Invoke($"Can't read the data folder {DataDir}: {e.Message}", true);
            return;
        }
        if (!force && stamp == _stamp) return;
        // Remember the stamp even if loading fails, so a broken file reports
        // once instead of every second; the next save tries again.
        _stamp = stamp;

        GameData data;
        try { data = GameData.Load(DataDir); }
        catch (Exception e) when (e is DataException or IOException)
        {
            GD.PushError($"Could not load data: {e.Message}");
            StatusChanged?.Invoke($"Data error, keeping the previous data: {e.Message}", true);
            return;
        }

        Data = data;
        Generator = new PawnGenerator(data);
        Catalog = new TraitCatalog(data);
        StatusChanged?.Invoke($"Data loaded from {DataDir} at {DateTime.Now:HH:mm:ss}.", false);
        Reloaded?.Invoke();
    }

    public string ReadFile(string name) => File.ReadAllText(Path.Combine(DataDir, name));

    /// <summary>Write a data file, keeping the previous version as name.bak, then reload.</summary>
    public void WriteFiles(params (string Name, string Text)[] files)
    {
        foreach (var (name, text) in files)
        {
            string path = Path.Combine(DataDir, name);
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(path, text);
        }
        ReloadIfChanged(force: true);
    }
}
