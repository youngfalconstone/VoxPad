using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxPad.Core.Remote;

/// <summary>
/// Remote Control API, shared by the app's named-pipe server and the reference client.
/// Transport: named pipe <c>\\.\pipe\VoxPadRemote</c>, one UTF-8 JSON object per line, one response line per request.
/// Plain-text shorthand is accepted too ("play 3", "stop", "pause", "playmode mic", "list", "status").
/// </summary>
public static class Protocol
{
    public const string PipeName = "VoxPadRemote";
    public const int Version = 1;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public enum Command { Version, Status, List, Play, Stop, Pause, PlayMode, Random, Volume, Categories }
    public enum Target { Auto, Mic, Speakers, Both }

    public sealed class Request
    {
        public Command Cmd { get; set; }
        public int? Index { get; set; }
        public string? Name { get; set; }
        public Guid? Id { get; set; }
        public Target? Target { get; set; }
        /// <summary>"both" | "mic" | "speakers" for PlayMode; omit to query.</summary>
        public string? Mode { get; set; }
        public Guid? Category { get; set; }
        public double? Level { get; set; }
    }

    public sealed class Response
    {
        public bool Ok { get; set; } = true;
        public string? Error { get; set; }
        public string? Version { get; set; }
        public string? Mode { get; set; }
        public bool? Playing { get; set; }
        public bool? Paused { get; set; }
        public int? Index { get; set; }
        public string? Tag { get; set; }
        public double? PositionSec { get; set; }
        public double? DurationSec { get; set; }
        public double? Level { get; set; }
        public List<SoundInfo>? Sounds { get; set; }
        public List<CategoryInfo>? Categories { get; set; }
    }

    public sealed class SoundInfo
    {
        public int Index { get; set; }
        public Guid Id { get; set; }
        public string Tag { get; set; } = "";
        /// <summary>"#RRGGBB" or null.</summary>
        public string? Color { get; set; }
        public double DurationSec { get; set; }
        public string? Hotkey { get; set; }
        public int PlayCount { get; set; }
        public bool Missing { get; set; }
    }

    public sealed class CategoryInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public Guid? Parent { get; set; }
        public int SoundCount { get; set; }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static Request Parse(string line)
    {
        line = line.Trim();
        if (line.StartsWith('{'))
            return JsonSerializer.Deserialize<Request>(line, Json) ?? throw new FormatException("Empty request");
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new FormatException("Empty request");
        var arg = parts.Length > 1 ? parts[1] : null;
        switch (parts[0].ToLowerInvariant())
        {
            case "version": return new Request { Cmd = Command.Version };
            case "status": return new Request { Cmd = Command.Status };
            case "list": return new Request { Cmd = Command.List };
            case "categories": return new Request { Cmd = Command.Categories };
            case "stop": return new Request { Cmd = Command.Stop };
            case "pause": return new Request { Cmd = Command.Pause };
            case "random": return new Request { Cmd = Command.Random };
            case "volume": return new Request { Cmd = Command.Volume, Level = arg == null ? null : double.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) };
            case "playmode": return new Request { Cmd = Command.PlayMode, Mode = arg };
            case "play":
                if (arg == null) throw new FormatException("play needs an index or name");
                var r = new Request { Cmd = Command.Play };
                var pieces = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (int.TryParse(pieces[0], out var idx)) r.Index = idx; else r.Name = arg;
                if (pieces.Length > 1 && r.Index != null && Enum.TryParse<Target>(pieces[1], true, out var t)) r.Target = t;
                return r;
            default: throw new FormatException($"Unknown command '{parts[0]}'");
        }
    }

    public static Response Fail(string error) => new() { Ok = false, Error = error };
}
