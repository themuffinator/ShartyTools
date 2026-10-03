using ShartyTools.Core.Formats;
using ShartyTools.Core.IO;

namespace ShartyTools.Core.Jams;

public sealed record Finding(string Severity, string Code, string Map, string Message);
public sealed record ValidationReport(IReadOnlyList<Finding> Findings, int AssetCount, long TotalBytes)
{
    public bool HasErrors => Findings.Any(f => f.Severity == "error");
    public override string ToString() => $"{AssetCount} assets · {TotalBytes / 1024.0 / 1024.0:F1} MiB · " +
        $"{Findings.Count(f => f.Severity == "error")} errors · {Findings.Count(f => f.Severity == "warning")} warnings\n\n" +
        string.Join('\n', Findings.Select(f => $"{f.Severity.ToUpperInvariant()} [{f.Code}] {(f.Map.Length == 0 ? "" : f.Map + ": ")}{f.Message}"));
}

public static class JamValidator
{
    public static readonly string[] ReservedNames = ["mapdb.json", "sharty-manifest.json", "sharty-credits.txt"];

    public static ValidationReport Validate(JamProject project, string projectPath)
    {
        var findings = new List<Finding>();
        void Add(string level, string code, string map, string message) => findings.Add(new Finding(level, code, map, message));
        AssetCatalog content;
        AssetCatalog reference;
        try
        {
            project.ValidateStructure();
            _ = MapDatabase.Export(project);
            content = AssetCatalog.Create(project.ResolveSources(projectPath));
            reference = AssetCatalog.Create(project.ResolveSources(projectPath, true), true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Add("error", "PROJECT", "", error.Message);
            return new ValidationReport(findings, 0, 0);
        }
        if (project.Maps.Count == 0) Add("error", "NO_MAPS", "", "Add at least one map to the jam.");
        if (project.StartMap.Length > 0 && !project.Maps.Any(m => GamePath.MapFileFromLaunch(m.Bsp) == GamePath.MapFileFromLaunch(project.StartMap)))
            Add("error", "START_MAP", "", "The start map must be listed in the jam.");
        if (project.Maps.Any(m => m.Sp || m.Coop) && project.StartMap.Length == 0)
            Add("warning", "START_MAP", "", "No start map selected; the first listed map will launch the generated episode.");
        foreach (var name in ReservedNames)
            if (content.Files.ContainsKey(name)) Add("error", "RESERVED", "", $"'{name}' is generated. Import existing mapdb metadata, then exclude its source file.");
        foreach (var duplicate in content.Duplicates.Distinct()) Add("info", "DEDUPLICATED", "", $"Identical copies of '{duplicate}' will be packaged once.");
        foreach (var asset in content.Files.Values)
        {
            if (asset.Name != asset.Name.ToLowerInvariant())
                Add("warning", "PATH_CASE", "", $"'{asset.Name}' uses uppercase characters; check on case-sensitive engines.");
            if (asset.Name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) && asset.Name.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase) &&
                !project.Maps.Any(m => string.Equals($"maps/{GamePath.MapFileFromLaunch(m.Bsp)}.bsp", asset.Name, StringComparison.OrdinalIgnoreCase)))
                Add("warning", "UNLISTED_MAP", "", $"'{asset.Name}' is packaged but is not listed in mapdb.");
        }
        bool Exists(string path) => content.Files.ContainsKey(path) || reference.Files.ContainsKey(path);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Dependency(string map, string path, params string[] alternatives)
        {
            try { GamePath.Validate(path); }
            catch (InvalidDataException e) { Add("error", "ASSET_PATH", map, e.Message); return; }
            if (!Exists(path) && !alternatives.Any(Exists) && missing.Add(map + ":" + path))
                Add("warning", "MISSING_ASSET", map, $"Cannot resolve '{path}' in content or reference sources.");
        }
        foreach (var map in project.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.Title)) Add("warning", "TITLE", map.Bsp, "Add a display title.");
            if (string.IsNullOrWhiteSpace(map.Author)) Add("warning", "CREDIT", map.Bsp, "Add a mapper credit.");
            if (!map.Sp && !map.Coop && !map.Dm) Add("warning", "GAME_MODE", map.Bsp, "No SP, co-op or deathmatch mode selected.");
            var mapFile = GamePath.MapFileFromLaunch(map.Bsp);
            foreach (var stage in map.Bsp.Split('+').SkipLast(1))
            {
                var cinematic = stage.TrimStart('*').Split('$')[0];
                if (cinematic.EndsWith(".cin", StringComparison.OrdinalIgnoreCase)) Dependency(map.Bsp, "video/" + cinematic);
            }
            if (!content.Files.TryGetValue($"maps/{mapFile}.bsp", out var asset))
            {
                Add("error", "MISSING_MAP", map.Bsp, "The BSP is missing from the packaged content.");
                continue;
            }
            try
            {
                var bsp = BspFile.Read(asset.ReadAll());
                if (bsp.Format == "QBSP" && project.Profile == "quake2-classic")
                    Add("error", "BSP_PROFILE", map.Bsp, "QBSP requires an extended engine; choose rerelease or compile IBSP for stock classic Quake II.");
                var entities = bsp.Entities;
                if (entities.Count == 0 || entities[0]["classname"] != "worldspawn" || entities.Count(e => e["classname"] == "worldspawn") != 1)
                    Add("error", "WORLDSPAWN", map.Bsp, "Expected exactly one worldspawn, as the first entity.");
                if ((map.Sp || map.Coop) && !entities.Any(e => e["classname"] == "info_player_start"))
                    Add("error", "SP_START", map.Bsp, "No info_player_start for single player/co-op.");
                if (map.Dm && !entities.Any(e => e["classname"] == "info_player_deathmatch"))
                    Add("error", "DM_START", map.Bsp, "No info_player_deathmatch for deathmatch.");
                if (map.Coop && !entities.Any(e => e["classname"] == "info_player_coop"))
                    Add("warning", "COOP_START", map.Bsp, "No dedicated co-op starts; verify fallback spawn behavior in the target engine.");
                if (map.Bots && !Exists($"bots/navigation/{mapFile}.nav"))
                    Add("warning", "BOT_NAV", map.Bsp, "Bots are enabled; verify the target engine's navigation data and behavior manually.");
                var targets = entities.Select(e => e["targetname"]).Where(s => s.Length > 0).ToHashSet(StringComparer.Ordinal);
                foreach (var (entity, index) in entities.Select((e, i) => (e, i)))
                {
                    foreach (var repeated in entity.RepeatedKeys ?? [])
                        Add(repeated.StartsWith('_') ? "info" : "warning", "REPEATED_KEY", map.Bsp, $"Entity {index} repeats '{repeated}'; checks use its last value, as the game does. Raw extraction preserves every occurrence.");
                    if (entity["classname"].Length == 0) Add("error", "CLASSNAME", map.Bsp, $"Entity {index} has no classname.");
                    foreach (var key in new[] { "target", "killtarget", "pathtarget", "deathtarget" })
                        if (entity[key].Length > 0 && !targets.Contains(entity[key]))
                            Add("warning", "DANGLING_TARGET", map.Bsp, $"Entity {index} ({entity["classname"]}) {key} '{entity[key]}' has no targetname match (custom scripts may resolve it).");
                    if (entity["classname"] == "target_changelevel" && entity["map"].Length > 0)
                    {
                        var destination = GamePath.MapFileFromLaunch(entity["map"]);
                        if (destination.Length > 0 && !Exists($"maps/{destination}.bsp"))
                            Add("warning", "MAP_LINK", map.Bsp, $"Changelevel destination '{destination}' is not available in content or references.");
                    }
                    foreach (var key in new[] { "model", "model2", "noise", "noise1", "noise2" })
                    {
                        var value = entity[key];
                        if (value.Length == 0 || value.StartsWith('*') || value.StartsWith('#') || !value.Contains('.')) continue;
                        var path = key.StartsWith("noise", StringComparison.Ordinal) && !value.StartsWith("sound/", StringComparison.Ordinal) ? "sound/" + value : value;
                        Dependency(map.Bsp, path);
                    }
                }
                foreach (var texture in bsp.Textures)
                    Dependency(map.Bsp, $"textures/{texture}.wal", $"textures/{texture}.tga", $"textures/{texture}.png");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Add("error", "BSP_READ", map.Bsp, error.Message);
            }
        }
        foreach (var asset in content.Files.Values.Where(f => f.Name.EndsWith(".md2", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var model = Md2Model.Read(asset.ReadAll());
                if (!model.ClassicCompatible && project.Profile == "quake2-classic")
                    Add("error", "MD2_PROFILE", asset.Name, "This model exceeds 512 frames and requires a rerelease-compatible engine.");
                foreach (var skin in model.Skins.Where(s => s.Length > 0)) Dependency(asset.Name, skin);
            }
            catch (Exception error) when (error is IOException or ArgumentException)
            { Add("error", "MD2_READ", asset.Name, error.Message); }
        }
        if (project.ReferenceSources.Count == 0)
            Add("info", "NO_REFERENCES", "", "Add read-only baseq2 folders/PAKs to resolve stock assets. References are never packaged.");
        return new ValidationReport(findings, content.Files.Count, content.Files.Values.Sum(f => f.Length));
    }

    public static int DiscoverMaps(JamProject project, string projectPath)
    {
        var catalog = AssetCatalog.Create(project.ResolveSources(projectPath));
        var added = 0;
        foreach (var file in catalog.Files.Values.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (!file.Name.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) || !file.Name.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase)) continue;
            var name = file.Name[5..^4];
            if (project.Maps.Any(m => string.Equals(GamePath.MapFileFromLaunch(m.Bsp), name, StringComparison.OrdinalIgnoreCase))) continue;
            var bsp = BspFile.Read(file.ReadAll());
            var world = bsp.Entities.FirstOrDefault(e => e["classname"] == "worldspawn");
            project.Maps.Add(new JamMap
            {
                Bsp = name, Title = string.IsNullOrEmpty(world?["message"]) ? name : world["message"],
                Author = world?["author"] ?? "", Episode = project.Id,
                Sp = bsp.Entities.Any(e => e["classname"] == "info_player_start"),
                Coop = bsp.Entities.Any(e => e["classname"] == "info_player_coop"),
                Dm = bsp.Entities.Any(e => e["classname"] == "info_player_deathmatch")
            });
            added++;
        }
        return added;
    }
}
