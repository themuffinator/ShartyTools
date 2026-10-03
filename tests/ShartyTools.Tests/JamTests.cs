using System.IO.Compression;
using System.Text.Json.Nodes;
using ShartyTools.Core.IO;
using ShartyTools.Core.Jams;

namespace ShartyTools.Tests;

public sealed class JamTests
{
    private static JamProject Project() => new()
    {
        Id = "testjam", Title = "Test Jam", StartMap = "test",
        Maps = [new() { Bsp = "test", Title = "The Test Chamber", Author = "Test Mapper", Sp = true }]
    };

    [Fact]
    public void BuildsDeterministicZipWithManifestCreditsAndMapdb()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp());
        folder.WriteText("reference/stock.txt", "stock assets are never packaged");
        var project = Project();
        project.ReferenceSources = ["reference"];
        var first = JamBuilder.Build(project, folder.PathFor("jam.json"), folder.PathFor("first.zip"));
        var second = JamBuilder.Build(project, folder.PathFor("jam.json"), folder.PathFor("second.zip"));
        Assert.Equal(first.Sha256, second.Sha256);
        using var archive = ZipFile.OpenRead(first.Path);
        Assert.Equal(["testjam/mapdb.json", "testjam/maps/test.bsp", "testjam/sharty-credits.txt", "testjam/sharty-manifest.json"], archive.Entries.Select(e => e.FullName));
        using var reader = new StreamReader(archive.GetEntry("testjam/sharty-manifest.json")!.Open());
        var manifest = JsonNode.Parse(reader.ReadToEnd())!;
        Assert.Equal(3, manifest["files"]!.AsArray().Count);
        Assert.All(manifest["files"]!.AsArray(), file => Assert.Equal(64, file!["sha256"]!.GetValue<string>().Length));
    }

    [Fact]
    public void PakRoundTripAndClassicProfileOmitRereleaseMapdb()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp());
        var project = Project();
        project.Profile = "quake2-classic";
        var result = JamBuilder.Build(project, folder.PathFor("jam.json"), folder.PathFor("pak0.pak"));
        var assets = AssetCatalog.Create([result.Path]);
        Assert.Equal(TestData.Bsp(), assets.Files["maps/test.bsp"].ReadAll());
        Assert.False(assets.Files.ContainsKey("mapdb.json"));
    }

    [Fact]
    public void SourceConflictsAreErrorsButIdenticalFilesDeduplicate()
    {
        using var folder = new TestFolder();
        folder.WriteText("one/credits.txt", "credit");
        folder.WriteText("two/credits.txt", "credit");
        var catalog = AssetCatalog.Create([folder.PathFor("one"), folder.PathFor("two")]);
        Assert.Single(catalog.Files);
        Assert.Single(catalog.Duplicates);
        folder.WriteText("two/credits.txt", "different");
        Assert.Throws<InvalidDataException>(() => AssetCatalog.Create([folder.PathFor("one"), folder.PathFor("two")]));
    }

    [Fact]
    public void SourceCaseCollisionsFailEvenWithIdenticalContent()
    {
        using var folder = new TestFolder();
        folder.WriteText("one/CREDIT.txt", "credit");
        folder.WriteText("two/credit.txt", "credit");
        Assert.Throws<InvalidDataException>(() => AssetCatalog.Create([folder.PathFor("one"), folder.PathFor("two")]));
    }

    [Fact]
    public void PakRejectsTraversalAndOutOfBoundsDirectory()
    {
        using var folder = new TestFolder();
        var bytes = new byte[76];
        "PACK"u8.CopyTo(bytes);
        TestData.Int(bytes, 4, 12);
        TestData.Int(bytes, 8, 64);
        System.Text.Encoding.ASCII.GetBytes("../evil").CopyTo(bytes, 12);
        TestData.Int(bytes, 68, 12);
        var path = folder.Write("bad.pak", bytes);
        Assert.Throws<InvalidDataException>(() => AssetCatalog.ReadPak(path));
        TestData.Int(bytes, 4, int.MaxValue);
        folder.Write("bad.pak", bytes);
        Assert.Throws<InvalidDataException>(() => AssetCatalog.ReadPak(path));
    }

    [Fact]
    public void ChecksSpawnPointsTargetsTransitionsAndTextureReferences()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp("""
            { "classname" "worldspawn" }
            { "classname" "trigger_once" "target" "missing" }
            { "classname" "target_changelevel" "map" "*gone$start" }
            """, texture: "test/wall"));
        var project = Project();
        var report = JamValidator.Validate(project, folder.PathFor("jam.json"));
        Assert.True(report.HasErrors);
        foreach (var code in new[] { "SP_START", "DANGLING_TARGET", "MAP_LINK", "MISSING_ASSET" }) Assert.Contains(report.Findings, f => f.Code == code);
        Assert.Throws<InvalidDataException>(() => JamBuilder.Build(project, folder.PathFor("jam.json"), folder.PathFor("out.zip")));
        Assert.False(File.Exists(folder.PathFor("out.zip")));
    }

    [Fact]
    public void ReferenceAssetsResolveWithoutBeingPackaged()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp(texture: "test/wall"));
        folder.WriteText("reference/textures/test/wall.wal", "reference");
        var project = Project();
        project.ReferenceSources = ["reference"];
        Assert.DoesNotContain(JamValidator.Validate(project, folder.PathFor("jam.json")).Findings, f => f.Code == "MISSING_ASSET");
    }

    [Fact]
    public void BuildsNeverWriteInsideContentOrReferenceTrees()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp());
        Assert.Throws<InvalidDataException>(() => JamBuilder.Build(Project(), folder.PathFor("jam.json"), folder.PathFor("content/jam.zip")));
    }

    [Fact]
    public void ExtendedBspIsRejectedForClassicAndSupportedForRerelease()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp(format: "QBSP"));
        var project = Project();
        Assert.False(JamValidator.Validate(project, folder.PathFor("jam.json")).HasErrors);
        project.Profile = "quake2-classic";
        Assert.Contains(JamValidator.Validate(project, folder.PathFor("jam.json")).Findings, f => f.Code == "BSP_PROFILE");
    }

    [Fact]
    public void DiscoveryDoesNotOverwriteExistingMetadataAndKeepsOrder()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp());
        folder.Write("content/maps/second.bsp", TestData.Bsp());
        var project = Project();
        project.Maps[0].Title = "Curated title";
        Assert.Equal(1, JamValidator.DiscoverMaps(project, folder.PathFor("jam.json")));
        Assert.Equal("test", project.Maps[0].Bsp);
        Assert.Equal("Curated title", project.Maps[0].Title);
        Assert.Equal("Test Mapper", project.Maps[1].Author);
    }

    [Fact]
    public void StrictModeBlocksWarningsAndNormalBuildReportsThem()
    {
        using var folder = new TestFolder();
        folder.Write("content/maps/test.bsp", TestData.Bsp());
        var project = Project();
        project.Maps[0].Author = "";
        Assert.Throws<InvalidDataException>(() => JamBuilder.Build(project, folder.PathFor("jam.json"), folder.PathFor("strict.zip"), true));
        var result = JamBuilder.Build(project, folder.PathFor("jam.json"), folder.PathFor("normal.zip"));
        Assert.Contains(result.Report.Findings, f => f.Code == "CREDIT");
    }
}
