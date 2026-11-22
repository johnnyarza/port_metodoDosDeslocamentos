using System.Text.Json;
using System.Text.Json.Serialization;
using FrameAnalysis.Core.Model;

namespace FrameAnalysis.Core.Data;

/// <summary>
/// Saves a frame model as JSON. In WBeamS16App this would be one column of the task row (like inputs_json);
/// here it is a file. Loading never trusts the file: <see cref="Load"/> returns the validation problems with the model.
/// </summary>
public static class FrameModelJson
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class Envelope
    {
        public int Version { get; set; }
        public FrameModel Model { get; set; } = new();
    }

    public static string Serialize(FrameModel model) =>
        JsonSerializer.Serialize(new Envelope { Version = CurrentVersion, Model = model }, Options);

    /// <summary>Reads a model. Throws <see cref="JsonException"/> when the text is not a frame file.</summary>
    public static FrameModel Deserialize(string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, Options) ?? throw new JsonException("The file is empty.");
        if (envelope.Version is < 1 or > CurrentVersion)
            throw new JsonException($"Unsupported frame file version {envelope.Version}.");
        var m = envelope.Model ?? throw new JsonException("The file has no model.");
        // A hand-edited file may say "nodes": null; treat missing parts as empty.
        m.Section ??= new();
        m.Material ??= new();
        m.Nodes ??= [];
        m.Members ??= [];
        m.LoadCases ??= [];
        m.NodalLoads ??= [];
        m.MemberLoads ??= [];
        m.Combinations ??= [];
        foreach (var c in m.Combinations) c.Factors ??= [];
        if (m.Nodes.Contains(null!) || m.Members.Contains(null!) || m.LoadCases.Contains(null!)
            || m.NodalLoads.Contains(null!) || m.MemberLoads.Contains(null!) || m.Combinations.Contains(null!))
            throw new JsonException("The file has empty entries.");
        return m;
    }

    /// <summary>Reads a model and checks it. The model is returned even when it has problems, so the user can fix them.</summary>
    public static (FrameModel Model, List<string> Problems) Load(string json)
    {
        var model = Deserialize(json);
        return (model, model.Validate());
    }
}
