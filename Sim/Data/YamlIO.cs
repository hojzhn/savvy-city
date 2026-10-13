using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace YouWillDate.Sim.Data;

/// <summary>
/// Reading and writing the data files. Writing keeps short maps and lists on
/// one line (<c>weights: { a: 1, b: 2 }</c>) so saved files stay readable.
/// </summary>
public static class YamlIO
{
    static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(
            DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
        .WithQuotingNecessaryStrings()
        .WithTypeConverter(new DoubleConverter())
        .WithEventEmitter(next => new FlowEmitter(next))
        .WithIndentedSequences()
        .Build();

    public static T Parse<T>(string file, string text)
    {
        try
        {
            return Deserializer.Deserialize<T>(text) ?? throw new DataException($"{file}: file is empty");
        }
        catch (YamlException e)
        {
            throw new DataException($"{file} line {e.Start.Line}: {e.InnerException?.Message ?? e.Message}");
        }
    }

    /// <summary>Serialize, with an optional comment header (kept as-is) on top.</summary>
    public static string Write<T>(T value, string? header = null)
    {
        string body = Serializer.Serialize(value!);
        return string.IsNullOrWhiteSpace(header) ? body : header.TrimEnd() + "\n\n" + body;
    }

    /// <summary>Deep copy through YAML, so edits to the copy never touch the original.</summary>
    public static T Clone<T>(T value) => Deserializer.Deserialize<T>(Serializer.Serialize(value!));

    /// <summary>The comment block at the top of a file, to carry over when it is rewritten.</summary>
    public static string LeadingComments(string text) =>
        string.Join("\n", text.Replace("\r\n", "\n").Split('\n')
            .TakeWhile(l => l.TrimStart().StartsWith('#') || l.Trim().Length == 0));

    /// <summary>
    /// Parse the inside of a one-line YAML map, such as
    /// <c>self.education: [masters, bachelors], year: "&gt;=1995"</c>.
    /// </summary>
    public static Dictionary<string, T> ParseInlineMap<T>(string inner)
    {
        if (string.IsNullOrWhiteSpace(inner)) return new();
        try
        {
            return Deserializer.Deserialize<Dictionary<string, T>>("{" + inner + "}") ?? new();
        }
        catch (YamlException e)
        {
            throw new DataException($"can't read '{inner}': {e.InnerException?.Message ?? e.Message}");
        }
    }

    /// <summary>The inside of a one-line YAML map: the reverse of <see cref="ParseInlineMap{T}"/>.</summary>
    public static string FormatInlineMap<T>(Dictionary<string, T>? map)
    {
        if (map is null || map.Count == 0) return "";
        string s = Serializer.Serialize(map).Trim();
        return s.StartsWith('{') && s.EndsWith('}') ? s[1..^1].Trim() : s;
    }

    /// <summary>Writes small maps and lists in flow style.</summary>
    sealed class FlowEmitter(IEventEmitter next) : ChainedEventEmitter(next)
    {
        static readonly HashSet<Type> FlowMaps =
            [typeof(Dictionary<string, double>), typeof(Dictionary<string, object>), typeof(Dictionary<string, string>),
             typeof(Dictionary<object, object>), typeof(Correlation), typeof(EraEntry), typeof(Life.StageDto)];

        public override void Emit(MappingStartEventInfo eventInfo, IEmitter emitter)
        {
            if (FlowMaps.Contains(eventInfo.Source.Type)) eventInfo.Style = MappingStyle.Flow;
            base.Emit(eventInfo, emitter);
        }

        public override void Emit(SequenceStartEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Type == typeof(List<string>) || eventInfo.Source.Type == typeof(List<object>))
                eventInfo.Style = SequenceStyle.Flow;
            base.Emit(eventInfo, emitter);
        }
    }

    /// <summary>Shortest round-trip form for doubles (0.1, not 0.10000000000000001).</summary>
    sealed class DoubleConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(double);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Consume<Scalar>();
            return double.Parse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) =>
            emitter.Emit(new Scalar(((double)value!).ToString("R", CultureInfo.InvariantCulture)));
    }
}
