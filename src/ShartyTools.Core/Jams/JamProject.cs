using System.Text.Json;
using System.Text.Json.Nodes;
using ShartyTools.Core.IO;

namespace ShartyTools.Core.Jams;

public sealed class JamMap
{
    public string Bsp { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Episode { get; set; } = "";
    public bool Sp { get; set; } = true;
    public bool Coop { get; set; }
    public bool Dm { get; set; }
    public bool Bots { get; set; }
    public JsonObject Extra { get; set; } = new();
    public override string ToString() => $"{Bsp}  ·  {Title}";
}

public sealed class JamProject
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "myjam";
    public string Title { get; set; } = "Untitled Map-Center jam";
    public string Profile { get; set; } = "quake2-rerelease";
    public string StartMap { get; set; } = "";
    public List<string> Sources { get; set; } = ["content"];
    public List<string> ReferenceSources { get; set; } = [];
    public List<JamMap> Maps { get; set; } = [];
    public JsonObject MapDbTemplate { get; set; } = new();
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static JamProject Load(string path)
    {
        var project = JsonSerializer.Deserialize<JamProject>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Empty jam project.");
        project.ValidateStructure();
        return project;
    }

    public void Save(string path, bool overwrite = false)
    {
        ValidateStructure();
        AtomicFile.WriteText(path, JsonSerializer.Serialize(this, JsonOptions) + "\n", overwrite);
    }

    public void ValidateStructure()
    {
        if (SchemaVersion != 1) throw new InvalidDataException($"Unsupported project schema {SchemaVersion}; expected 1.");
        GamePath.Validate(Id, 40);
        if (Id.Contains('/') || Id.Contains('.')) throw new InvalidDataException("Jam ID must be a single folder name without dots.");
        if (string.IsNullOrWhiteSpace(Title)) throw new InvalidDataException("Give the jam a title.");
        if (StartMap is null) throw new InvalidDataException("startMap cannot be null; use an empty string if unset.");
        if (Profile is not ("quake2-classic" or "quake2-rerelease")) throw new InvalidDataException("Choose quake2-classic or quake2-rerelease.");
        if (Sources is null || ReferenceSources is null || Maps is null || MapDbTemplate is null ||
            Sources.Any(string.IsNullOrWhiteSpace) || ReferenceSources.Any(string.IsNullOrWhiteSpace) ||
            Maps.Any(m => m is null || m.Bsp is null || m.Title is null || m.Author is null || m.Episode is null || m.Extra is null))
            throw new InvalidDataException("Project collections and their fields cannot be null or contain empty source paths.");
        if (!string.IsNullOrEmpty(StartMap)) GamePath.MapLaunch(StartMap);
        foreach (var map in Maps) GamePath.MapLaunch(map.Bsp);
    }

    public string[] ResolveSources(string projectPath, bool reference = false)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        return (reference ? ReferenceSources : Sources).Select(p => Path.GetFullPath(p, directory)).ToArray();
    }
}
