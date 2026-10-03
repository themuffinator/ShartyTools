using System.Text.Json;
using System.Text.Json.Nodes;
using ShartyTools.Core.IO;

namespace ShartyTools.Core.Jams;

public static class MapDatabase
{
    private static readonly HashSet<string> EditedKeys = new(StringComparer.Ordinal)
        { "bsp", "title", "episode", "sp", "coop", "dm", "bot", "bots" };

    public static void Import(JamProject project, string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new InvalidDataException("mapdb must be a JSON object.");
        if (root["maps"] is not JsonArray maps) throw new InvalidDataException("mapdb needs a maps array.");
        if (root["episodes"] is not JsonArray episodes) throw new InvalidDataException("mapdb needs an episodes array.");
        var episodeIds = ValidateEpisodes(episodes);
        var imported = new List<JamMap>();
        foreach (var item in maps)
        {
            if (item is not JsonObject map) throw new InvalidDataException("Each mapdb map must be an object.");
            var bsp = String(map, "bsp");
            GamePath.MapLaunch(bsp);
            var episode = String(map, "episode");
            if (!episodeIds.Contains(episode)) throw new InvalidDataException($"Map '{bsp}' references unknown episode '{episode}'.");
            var extra = (JsonObject)map.DeepClone();
            foreach (var key in EditedKeys.Where(k => k is not ("bot" or "bots"))) extra.Remove(key);
            imported.Add(new JamMap
            {
                Bsp = bsp, Title = String(map, "title"), Episode = episode,
                Author = project.Maps.FirstOrDefault(m => m.Bsp == bsp)?.Author ?? "",
                Sp = Flag(map, "sp"), Coop = Flag(map, "coop"), Dm = Flag(map, "dm"),
                Bots = Flag(map, "bots") | Flag(map, "bot"), Extra = extra
            });
        }
        // Commit only after the entire document has passed validation.
        project.MapDbTemplate = root;
        project.Maps = imported;
    }

    public static string Export(JamProject project)
    {
        project.ValidateStructure();
        var root = (JsonObject)project.MapDbTemplate.DeepClone();
        if (root["episodes"] is not JsonArray) root["episodes"] = new JsonArray();
        var episodes = (JsonArray)root["episodes"]!;
        var ids = ValidateEpisodes(episodes);
        if (!ids.Contains(project.Id))
        {
            var first = string.IsNullOrEmpty(project.StartMap) ? project.Maps.FirstOrDefault()?.Bsp : project.StartMap;
            var episode = new JsonObject
            {
                ["id"] = project.Id, ["name"] = project.Title,
                ["command"] = first is null ? "" : $"map {first}",
                ["needsSkillSelect"] = project.Maps.Any(m => m.Sp || m.Coop)
            };
            episodes.Add(episode);
            ids.Add(project.Id);
        }
        var maps = new JsonArray();
        foreach (var map in project.Maps)
        {
            var episode = string.IsNullOrWhiteSpace(map.Episode) ? project.Id : map.Episode;
            if (!ids.Contains(episode)) throw new InvalidDataException($"Map '{map.Bsp}' references unknown episode '{episode}'.");
            var item = (JsonObject)map.Extra.DeepClone();
            foreach (var key in EditedKeys) item.Remove(key);
            item["bsp"] = map.Bsp;
            item["title"] = map.Title;
            item["episode"] = episode;
            item["sp"] = map.Sp;
            item["coop"] = map.Coop;
            item["dm"] = map.Dm;
            // Stock rerelease assets use 'bot'; compatible engines also use 'bots'.
            // Keep the original spelling when importing; new entries use stock 'bot'.
            var botKey = map.Extra.ContainsKey("bots") ? "bots" : "bot";
            item[botKey] = map.Bots;
            if (map.Extra.ContainsKey("bot") && map.Extra.ContainsKey("bots")) item["bot"] = map.Bots;
            maps.Add(item);
        }
        root["maps"] = maps;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    private static HashSet<string> ValidateEpisodes(JsonArray episodes)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in episodes)
        {
            if (item is not JsonObject episode || !ids.Add(String(episode, "id")) || string.IsNullOrWhiteSpace(String(episode, "id")))
                throw new InvalidDataException("mapdb episode IDs must be nonempty and unique.");
        }
        return ids;
    }

    private static string String(JsonObject node, string key)
    {
        if (node[key] is null) return "";
        if (node[key] is JsonValue value && value.TryGetValue<string>(out var text)) return text;
        throw new InvalidDataException($"mapdb '{key}' must be a string.");
    }

    private static bool Flag(JsonObject node, string key)
    {
        if (node[key] is null) return false;
        if (node[key] is JsonValue value && value.TryGetValue<bool>(out var flag)) return flag;
        throw new InvalidDataException($"mapdb '{key}' must be a boolean.");
    }
}
