namespace YouWillDate.Sim.Data;

/// <summary>A data file is malformed or inconsistent. The message names the file and entry.</summary>
public sealed class DataException(string message) : Exception(message);
