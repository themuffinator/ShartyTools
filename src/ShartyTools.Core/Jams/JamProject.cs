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
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = "myjam";
    public string Title { get; set; } = "Untitled Map-Center jam";
    public string Profile { get; set; } = "quake2-rerelease";
    public string StartMap { get; set; } = "";
    public List<string> Sources { get; set; } = ["content"];
    public List<string> ReferenceSources { get; set; } = [];
    public Dictionary<string, SourceSettings> SourceSettings { get; set; } = new(StringComparer.Ordinal);
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
        SchemaVersion = CurrentSchemaVersion;
        if (overwrite && File.Exists(path) && SchemaVersion == CurrentSchemaVersion)
        {
            GamePath.RejectLinks(path);
            var original = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(original);
            if (document.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == 1)
            {
                var backup = path + ".schema1.bak";
                GamePath.RejectLinks(backup);
                if (File.Exists(backup))
                {
                    if (!File.ReadAllBytes(backup).AsSpan().SequenceEqual(original))
                        throw new IOException($"A different schema-1 backup already exists: {backup}. Use Save As or move the backup first.");
                }
                else AtomicFile.Write(backup, output => output.Write(original));
            }
        }
        AtomicFile.WriteText(path, JsonSerializer.Serialize(this, JsonOptions) + "\n", overwrite);
    }

    public void ValidateStructure()
    {
        if (SchemaVersion is not (1 or CurrentSchemaVersion)) throw new InvalidDataException($"Unsupported project schema {SchemaVersion}; expected 1 or {CurrentSchemaVersion}.");
        GamePath.Validate(Id, 40);
        if (Id.Contains('/') || Id.Contains('.')) throw new InvalidDataException("Jam ID must be a single folder name without dots.");
        if (string.IsNullOrWhiteSpace(Title)) throw new InvalidDataException("Give the jam a title.");
        if (StartMap is null) throw new InvalidDataException("startMap cannot be null; use an empty string if unset.");
        if (Profile is not ("quake2-classic" or "quake2-rerelease")) throw new InvalidDataException("Choose quake2-classic or quake2-rerelease.");
        if (Sources is null || ReferenceSources is null || SourceSettings is null || Maps is null || MapDbTemplate is null ||
            Sources.Any(string.IsNullOrWhiteSpace) || ReferenceSources.Any(string.IsNullOrWhiteSpace) ||
            Maps.Any(m => m is null || m.Bsp is null || m.Title is null || m.Author is null || m.Episode is null || m.Extra is null))
            throw new InvalidDataException("Project collections and their fields cannot be null or contain empty source paths.");
        if (Sources.Distinct(StringComparer.Ordinal).Count() != Sources.Count) throw new InvalidDataException("Each content source must be listed only once.");
        if (SchemaVersion == 1 && SourceSettings.Count > 0) throw new InvalidDataException("Source settings require project schema 2.");
        foreach (var (source, settings) in SourceSettings)
        {
            if (!Sources.Contains(source, StringComparer.Ordinal) || settings is null || settings.Root is null || settings.ExcludedFiles is null)
                throw new InvalidDataException("Source settings must name an existing content source and cannot be null.");
            if (settings.Root.Length > 0) ZipSource.ValidateRelativePath(settings.Root);
            foreach (var name in settings.ExcludedFiles) ZipSource.ValidateRelativePath(name);
        }
        if (!string.IsNullOrEmpty(StartMap)) GamePath.MapLaunch(StartMap);
        foreach (var map in Maps) GamePath.MapLaunch(map.Bsp);
    }

    public string[] ResolveSources(string projectPath, bool reference = false)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        return (reference ? ReferenceSources : Sources).Select(p => Path.GetFullPath(p, directory)).ToArray();
    }
}

public sealed class SourceSettings
{
    public string Root { get; set; } = "";
    public List<string> ExcludedFiles { get; set; } = [];
}
