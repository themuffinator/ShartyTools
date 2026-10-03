using System.Text.Json;
using System.Text.Json.Nodes;
using ShartyTools.Core.Jams;
using ShartyTools.Desktop;

namespace ShartyTools.Tests;

public sealed class MetadataTests
{
    private const string Mapdb = """
        {"customRoot":{"keep":42}, "episodes":[{"id":"pack", "command":"exec custom.cfg", "name":"Pack", "activity":"custom"}],
         "maps":[{"bsp":"test", "title":"Test", "episode":"pack", "sp":true,"bot":true,"unit":7,"start_items":"blaster","custom":{"a":1}}]}
        """;

    [Fact]
    public void MapdbRoundTripPreservesUnknownFieldsCommandsAndBotSpelling()
    {
        var project = new JamProject();
        MapDatabase.Import(project, Mapdb);
        project.Maps[0].Title = "Edited";
        var root = JsonNode.Parse(MapDatabase.Export(project))!;
        Assert.Equal(42, root["customRoot"]!["keep"]!.GetValue<int>());
        Assert.Equal("exec custom.cfg", root["episodes"]![0]!["command"]!.GetValue<string>());
        Assert.Equal("custom", root["episodes"]![0]!["activity"]!.GetValue<string>());
        var map = root["maps"]![0]!;
        Assert.Equal("Edited", map["title"]!.GetValue<string>());
        Assert.Equal(7, map["unit"]!.GetValue<int>());
        Assert.Equal(1, map["custom"]!["a"]!.GetValue<int>());
        Assert.True(map["bot"]!.GetValue<bool>());
        Assert.Null(map["bots"]);
    }

    [Fact]
    public void InvalidImportLeavesProjectUntouched()
    {
        var project = new JamProject();
        MapDatabase.Import(project, Mapdb);
        var before = JsonSerializer.Serialize(project, JamProject.JsonOptions);
        Assert.Throws<InvalidDataException>(() => MapDatabase.Import(project, Mapdb.Replace("\"sp\":true", "\"sp\":\"false\"")));
        Assert.Equal(before, JsonSerializer.Serialize(project, JamProject.JsonOptions));
    }

    [Fact]
    public void CinematicAndUnitLaunchExpressionsSurviveMapdbRoundTrip()
    {
        var project = new JamProject();
        MapDatabase.Import(project, Mapdb.Replace("\"test\"", "\"intro.cin+*test$entry\""));
        Assert.Equal("test", ShartyTools.Core.IO.GamePath.MapFileFromLaunch(project.Maps[0].Bsp));
        Assert.Equal("intro.cin+*test$entry", JsonNode.Parse(MapDatabase.Export(project))!["maps"]![0]!["bsp"]!.GetValue<string>());
        Assert.Throws<InvalidDataException>(() => ShartyTools.Core.IO.GamePath.MapLaunch("test;quit"));
    }

    [Fact]
    public void MultipleMenuListingsForOneBspKeepIndependentMetadata()
    {
        var root = JsonNode.Parse(Mapdb)!;
        var second = root["maps"]![0]!.DeepClone();
        second["title"] = "Chapter entry";
        second["unit"] = 2;
        second.AsObject().Remove("bot");
        second["bots"] = true;
        root["maps"]!.AsArray().Add(second);
        var project = new JamProject();
        MapDatabase.Import(project, root.ToJsonString());
        var exported = JsonNode.Parse(MapDatabase.Export(project))!["maps"]!.AsArray();
        Assert.Equal(2, exported.Count);
        Assert.Equal("Chapter entry", exported[1]!["title"]!.GetValue<string>());
        Assert.Null(exported[1]!["bot"]);
        Assert.True(exported[1]!["bots"]!.GetValue<bool>());
    }

    [Fact]
    public void ApplyMapdbKeepsMapperCreditsAndMarksProjectDirty()
    {
        var project = new JamProject();
        MapDatabase.Import(project, Mapdb);
        project.Maps[0].Author = "Mapper";
        var vm = new WorkspaceViewModel();
        vm.LoadProject(project, "");
        Assert.False(vm.JamDirty);
        vm.MapDbText = vm.MapDbText.Replace("\"Test\"", "\"New title\"");
        Assert.True(vm.JamDirty);
        Assert.Throws<InvalidOperationException>(() => vm.Snapshot());
        vm.ApplyMapDb();
        Assert.Equal("Mapper", vm.Maps[0].Author);
        Assert.Equal("New title", vm.Maps[0].Title);
        Assert.True(vm.JamDirty);
    }

    [Fact]
    public void SaveAsRebasesRelativePaths()
    {
        using var folder = new TestFolder();
        Directory.CreateDirectory(folder.PathFor("elsewhere"));
        var vm = new WorkspaceViewModel();
        vm.LoadProject(new JamProject(), folder.PathFor("first.json"));
        vm.SaveProject(folder.PathFor("elsewhere/copy.json"));
        Assert.Equal("../content", vm.SourcesText);
        Assert.Equal(folder.PathFor("content"), vm.Snapshot().ResolveSources(vm.ProjectPath)[0]);
        Assert.False(vm.JamDirty);
    }

    [Fact]
    public void SkinEditorDoesNotSilentlyDeleteEmptySlots()
    {
        var vm = new WorkspaceViewModel { SkinsText = "one.pcx\n\nthree.pcx" };
        Assert.Equal(["one.pcx", "", "three.pcx"], vm.SkinPaths());
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"sources\":null}")]
    [InlineData("{\"maps\":[null]}")]
    [InlineData("{\"startMap\":null}")]
    public void InvalidProjectSchemasFailClearly(string text)
    {
        using var folder = new TestFolder();
        Assert.Throws<InvalidDataException>(() => JamProject.Load(folder.WriteText("project.json", text)));
    }
}
