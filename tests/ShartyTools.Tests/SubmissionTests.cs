using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ShartyTools.Core.IO;
using ShartyTools.Core.Jams;
using ShartyTools.Desktop;
using CommandLine = ShartyTools.Cli.Program;

namespace ShartyTools.Tests;

public sealed class SubmissionTests
{
    private const string Mapdb = """
        {"episodes":[{"id":"testjam","command":"map test","custom":42}],
         "maps":[{"bsp":"test","title":"Imported title","episode":"testjam","sp":true,"unit":3}]}
        """;

    internal static string Zip(TestFolder folder, string name, params (string Path, byte[] Bytes)[] entries)
    {
        var path = folder.PathFor(name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryName, bytes) in entries)
        {
            using var output = archive.CreateEntry(entryName).Open();
            output.Write(bytes);
        }
        return path;
    }

    private static JamProject Project(params string[] sources) => new()
    {
        Id = "testjam", StartMap = "test", Sources = sources.ToList(),
        Maps = [new JamMap { Bsp = "test", Title = "Test", Author = "Mapper" }]
    };

    [Fact]
    public void ZipSubmissionImportsMetadataAndBuildsBothFormatsWithoutChangingSource()
    {
        using var folder = new TestFolder();
        var source = Zip(folder, "submission.zip", ("pack/maps/test.bsp", TestData.Bsp()),
            ("pack/mapdb.json", Encoding.UTF8.GetBytes(Mapdb)), ("pack/readme.txt", "Original credit"u8.ToArray()),
            ("private-notes.txt", "Not part of the mod"u8.ToArray()));
        var original = SHA256.HashData(File.ReadAllBytes(source));
        var project = Project("submission.zip");
        var projectPath = folder.PathFor("jam.json");
        JamContent.SetRoot(project, "submission.zip", "pack");
        Assert.Contains(JamValidator.Validate(project, projectPath).Findings, finding => finding.Code == "RESERVED");
        JamContent.ImportMapDb(project, projectPath, "submission.zip", "pack/mapdb.json");
        Assert.Equal("Imported title", project.Maps[0].Title);
        Assert.Equal("Mapper", project.Maps[0].Author);
        Assert.Contains("pack/mapdb.json", project.SourceSettings["submission.zip"].ExcludedFiles);
        Assert.False(JamValidator.Validate(project, projectPath).HasErrors);
        Assert.Equal(0, JamValidator.DiscoverMaps(project, projectPath));
        var zip = JamBuilder.Build(project, projectPath, folder.PathFor("out.zip"));
        var repeat = JamBuilder.Build(project, projectPath, folder.PathFor("again.zip"));
        Assert.Equal(zip.Sha256, repeat.Sha256);
        using var archive = ZipFile.OpenRead(zip.Path);
        Assert.Null(archive.GetEntry("testjam/private-notes.txt"));
        using var reader = new StreamReader(archive.GetEntry("testjam/sharty-manifest.json")!.Open());
        var manifest = JsonNode.Parse(reader.ReadToEnd())!;
        var bsp = manifest["files"]!.AsArray().Single(file => file!["path"]!.GetValue<string>() == "maps/test.bsp")!;
        Assert.Equal("pack/maps/test.bsp", bsp["origin"]!["entry"]!.GetValue<string>());
        Assert.Equal(1, bsp["origin"]!["sourceNumber"]!.GetValue<int>());
        Assert.DoesNotContain(folder.Root, manifest.ToJsonString());
        var pak = JamBuilder.Build(project, projectPath, folder.PathFor("out.pak"));
        var files = AssetCatalog.Create([pak.Path]);
        Assert.Equal(TestData.Bsp(), files.Files["maps/test.bsp"].ReadAll());
        Assert.Equal("Original credit", Encoding.UTF8.GetString(files.Files["readme.txt"].ReadAll()));
        Assert.Equal(3, JsonNode.Parse(files.Files["mapdb.json"].ReadAll())!["maps"]![0]!["unit"]!.GetValue<int>());
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(source)));
        project.Save(projectPath);
        Assert.Equal("pack", JamProject.Load(projectPath).SourceSettings["submission.zip"].Root);
    }

    [Fact]
    public void InventoryRemainsUsableDuringConflictsAndExclusionsSelectTheWantedCopy()
    {
        using var folder = new TestFolder();
        folder.Write("one/maps/test.bsp", TestData.Bsp());
        folder.WriteText("one/shared.txt", "old");
        folder.WriteText("two/shared.txt", "new");
        folder.WriteText("two/My Notes.txt", "private");
        var project = Project("one", "two");
        var path = folder.PathFor("jam.json");
        Assert.Equal(4, JamContent.Inspect(project, path).Count);
        Assert.Contains(JamValidator.Validate(project, path).Findings, finding => finding.Code == "PROJECT");
        JamContent.SetIncluded(project, "one", "shared.txt", false);
        JamContent.SetIncluded(project, "two", "My Notes.txt", false);
        var assets = JamContent.CreateCatalog(project, path);
        Assert.Equal("new", Encoding.UTF8.GetString(assets.Files["shared.txt"].ReadAll()));
        JamContent.SetIncluded(project, "one", "shared.txt", true);
        Assert.Throws<InvalidDataException>(() => JamContent.CreateCatalog(project, path));
        Assert.Equal("old", File.ReadAllText(folder.PathFor("one/shared.txt")));
    }

    [Fact]
    public void ExcludingMapAffectsDiscoveryValidationAndBuildTogether()
    {
        using var folder = new TestFolder();
        Zip(folder, "maps.zip", ("maps/test.bsp", TestData.Bsp()), ("maps/second.bsp", TestData.Bsp()));
        var project = Project("maps.zip");
        var path = folder.PathFor("jam.json");
        JamContent.SetIncluded(project, "maps.zip", "maps/second.bsp", false);
        Assert.Equal(0, JamValidator.DiscoverMaps(project, path));
        Assert.False(JamValidator.Validate(project, path).HasErrors);
        JamContent.SetIncluded(project, "maps.zip", "maps/test.bsp", false);
        Assert.Contains(JamValidator.Validate(project, path).Findings, finding => finding.Code == "MISSING_MAP");
        Assert.Throws<InvalidDataException>(() => JamBuilder.Build(project, path, folder.PathFor("out.zip")));
        Assert.False(File.Exists(folder.PathFor("out.zip")));
    }

    [Fact]
    public void InvalidArchiveMetadataImportIsTransactional()
    {
        using var folder = new TestFolder();
        Zip(folder, "bad.zip", ("mapdb.json", "{broken}"u8.ToArray()));
        var project = Project("bad.zip");
        var before = JsonSerializer.Serialize(project, JamProject.JsonOptions);
        Assert.ThrowsAny<JsonException>(() => JamContent.ImportMapDb(project, folder.PathFor("jam.json"), "bad.zip", "mapdb.json"));
        Assert.Equal(before, JsonSerializer.Serialize(project, JamProject.JsonOptions));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("maps\\test.bsp")]
    [InlineData("pack/./test")]
    [InlineData("pack//test")]
    public void ZipRejectsUnsafePaths(string name)
    {
        using var folder = new TestFolder();
        Assert.Throws<InvalidDataException>(() => ZipSource.Read(Zip(folder, "bad.zip", (name, [1]))));
    }

    [Theory]
    [InlineData("link")]
    [InlineData("encrypted")]
    [InlineData("entry-size")]
    [InlineData("entry-count")]
    [InlineData("directory-size")]
    [InlineData("multivolume")]
    [InlineData("unsupported-compression")]
    public void ZipRejectsUnboundedOrUnsupportedDirectories(string problem)
    {
        using var folder = new TestFolder();
        var path = Zip(folder, "bad.zip", ("data.txt", "data"u8.ToArray()));
        var bytes = File.ReadAllBytes(path);
        var central = FindCentral(bytes);
        var end = bytes.Length - 22;
        switch (problem)
        {
            case "link": TestData.Int(bytes, central + 38, unchecked((int)0xa1ff0000)); break;
            case "encrypted": bytes[central + 8] |= 1; bytes[6] |= 1; break;
            case "entry-size": TestData.Int(bytes, central + 24, (int)ZipSource.MaxEntryBytes + 1); TestData.Int(bytes, 22, (int)ZipSource.MaxEntryBytes + 1); break;
            case "entry-count": bytes[end + 8] = bytes[end + 10] = 0; break;
            case "directory-size": TestData.Int(bytes, end + 12, int.MaxValue); break;
            case "multivolume": bytes[end + 4] = 1; break;
            case "unsupported-compression": bytes[central + 10] = 99; break;
        }
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => ZipSource.Read(path));
    }

    [Fact]
    public void ZipRejectsDuplicateEntriesAndChecksCrcBeforeTrustingBytes()
    {
        using var folder = new TestFolder();
        Assert.Throws<InvalidDataException>(() => ZipSource.Read(Zip(folder, "duplicate.zip", ("data.txt", [1]), ("data.txt", [1]))));
        var path = Zip(folder, "crc.zip", ("data.txt", "original"u8.ToArray()));
        var bytes = File.ReadAllBytes(path);
        bytes[FindCentral(bytes) + 16] ^= 1;
        bytes[14] ^= 1;
        File.WriteAllBytes(path, bytes);
        var file = Assert.Single(ZipSource.Read(path));
        Assert.Throws<InvalidDataException>(() => file.ReadAll());
        Assert.Throws<InvalidDataException>(() => file.CopyAndHash());
    }

    [Fact]
    public void ZipRejectsExpandedContentBeyondAdvertisedLength()
    {
        using var folder = new TestFolder();
        var path = Zip(folder, "size.zip", ("data.txt", "original"u8.ToArray()));
        var bytes = File.ReadAllBytes(path);
        TestData.Int(bytes, FindCentral(bytes) + 24, 1);
        TestData.Int(bytes, 22, 1);
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => ZipSource.Read(path)[0].ReadAll());
    }

    [Fact]
    public void StoredZipEntriesAndEmptyFilesReadWithoutExtracting()
    {
        using var folder = new TestFolder();
        var path = folder.PathFor("stored.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            archive.CreateEntry("empty.txt", CompressionLevel.NoCompression);
            using var output = archive.CreateEntry("maps/test.bsp", CompressionLevel.NoCompression).Open();
            output.Write(TestData.Bsp());
        }
        var files = ZipSource.Read(path);
        Assert.Empty(files.Single(file => file.Name == "empty.txt").ReadAll());
        Assert.Equal(TestData.Bsp(), files.Single(file => file.Name == "maps/test.bsp").ReadAll());
        Assert.Single(Directory.GetFiles(folder.Root));
    }

    [Fact]
    public void ZipDataDescriptorsUseBoundedCentralDirectorySizes()
    {
        using var folder = new TestFolder();
        var path = Zip(folder, "streamed.zip", ("maps/test.bsp", TestData.Bsp()));
        var original = File.ReadAllBytes(path);
        var central = FindCentral(original);
        var streamed = new byte[original.Length + 16];
        original.AsSpan(0, central).CopyTo(streamed);
        TestData.Int(streamed, central, 0x08074b50);
        original.AsSpan(14, 12).CopyTo(streamed.AsSpan(central + 4));
        original.AsSpan(central).CopyTo(streamed.AsSpan(central + 16));
        streamed[6] |= 8;
        streamed[central + 16 + 8] |= 8;
        streamed.AsSpan(14, 12).Clear();
        TestData.Int(streamed, streamed.Length - 22 + 16, central + 16);
        File.WriteAllBytes(path, streamed);
        Assert.Equal(TestData.Bsp(), Assert.Single(ZipSource.Read(path)).ReadAll());
    }

    [Fact]
    public void ZipRejectsTotalExpandedLimitBeforeReadingPayloads()
    {
        using var folder = new TestFolder();
        var path = Zip(folder, "large.zip", Enumerable.Range(0, 17).Select(i => ($"file{i}.txt", "original"u8.ToArray())).ToArray());
        var bytes = File.ReadAllBytes(path);
        var position = FindCentral(bytes);
        for (var i = 0; i < 17; i++)
        {
            TestData.Int(bytes, position + 24, (int)ZipSource.MaxEntryBytes);
            var localOffset = TestData.Int(bytes, position + 42);
            TestData.Int(bytes, localOffset + 22, (int)ZipSource.MaxEntryBytes);
            position += 46 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 28)) +
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 30)) + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 32));
        }
        File.WriteAllBytes(path, bytes);
        Assert.Contains("8 GiB", Assert.Throws<InvalidDataException>(() => ZipSource.Read(path)).Message);
    }

    [Fact]
    public void ZipRejectsMismatchedLocalHeadersAndChangedArchiveAfterIndexing()
    {
        using var folder = new TestFolder();
        var path = Zip(folder, "source.zip", ("data.txt", "original"u8.ToArray()));
        var bytes = File.ReadAllBytes(path);
        bytes[30] = (byte)'X';
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => ZipSource.Read(path));
        bytes[30] = (byte)'d';
        File.WriteAllBytes(path, bytes);
        var file = Assert.Single(ZipSource.Read(path));
        File.AppendAllText(path, "changed");
        Assert.Throws<IOException>(() => file.ReadAll());
    }

    [Fact]
    public void ProjectSnapshotsDoNotDiscardSettingsDuringUnfinishedSourceEdits()
    {
        var project = Project("submission.zip");
        JamContent.SetIncluded(project, "submission.zip", "old.txt", false);
        var workspace = new WorkspaceViewModel();
        workspace.LoadProject(project, "");
        workspace.SourcesText = "";
        Assert.Empty(workspace.Snapshot().SourceSettings);
        Assert.True(workspace.JamDirty);
        workspace.SourcesText = "submission.zip";
        Assert.Contains("old.txt", workspace.Snapshot().SourceSettings["submission.zip"].ExcludedFiles);
        Assert.False(workspace.JamDirty);
    }

    [Fact]
    public void SavingLegacyProjectBacksUpExactOriginalAndSaveAsRebasesSettings()
    {
        using var folder = new TestFolder();
        const string legacy = "{\"schemaVersion\":1,\"sources\":[\"content\"]}";
        var path = folder.WriteText("legacy.json", legacy);
        var project = JamProject.Load(path);
        JamContent.SetIncluded(project, "content", "old.txt", false);
        project.Save(path, true);
        Assert.Equal(legacy, File.ReadAllText(path + ".schema1.bak"));
        Assert.Equal(2, JamProject.Load(path).SchemaVersion);
        var workspace = new WorkspaceViewModel();
        workspace.LoadProject(project, path);
        Directory.CreateDirectory(folder.PathFor("other"));
        workspace.SaveProject(folder.PathFor("other/copy.json"));
        Assert.False(workspace.JamDirty);
        Assert.Contains("old.txt", workspace.Snapshot().SourceSettings["../content"].ExcludedFiles);
        workspace.SaveProject(folder.PathFor("other/copy.json"));
        Assert.Equal(legacy, File.ReadAllText(path + ".schema1.bak"));
    }

    [Fact]
    public void LegacyBackupCollisionDoesNotOverwriteEitherDocument()
    {
        using var folder = new TestFolder();
        const string legacy = "{\"schemaVersion\":1}";
        var path = folder.WriteText("legacy.json", legacy);
        folder.WriteText("legacy.json.schema1.bak", "previous backup");
        Assert.Throws<IOException>(() => JamProject.Load(path).Save(path, true));
        Assert.Equal(legacy, File.ReadAllText(path));
        Assert.Equal("previous backup", File.ReadAllText(path + ".schema1.bak"));
    }

    [Fact]
    public void CliCanCurateZipSubmissionAndReturnsErrorsWithoutCrashing()
    {
        using var folder = new TestFolder();
        var path = folder.PathFor("jam.json");
        var archive = Zip(folder, "submission.zip", ("pack/maps/test.bsp", TestData.Bsp()), ("pack/mapdb.json", Encoding.UTF8.GetBytes(Mapdb)));
        Assert.Equal(0, CommandLine.Main(["jam", "new", path, "testjam", "Test Jam"]));
        Assert.Equal(0, CommandLine.Main(["jam", "add", path, archive, "--root", "pack"]));
        Assert.Equal(0, CommandLine.Main(["jam", "files", path, "--json"]));
        Assert.Equal(0, CommandLine.Main(["mapdb", "import-source", path, "2", "pack/mapdb.json"]));
        Assert.Equal(0, CommandLine.Main(["jam", "scan", path]));
        Assert.Equal(0, CommandLine.Main(["jam", "build", path, folder.PathFor("out.zip")]));
        Assert.Equal(2, CommandLine.Main(["jam", "add", path, archive]));
        Assert.Equal(2, CommandLine.Main(["jam", "exclude", path, "2", "does-not-exist"]));
        Assert.Equal(2, CommandLine.Main(["jam", "root", path, "2", "../escape"]));
        Assert.Equal(2, CommandLine.Main(["jam", "add", path, folder.WriteText("invalid.zip", "bad")]));
    }

    private static int FindCentral(byte[] bytes)
    {
        for (var i = 0; i + 4 <= bytes.Length; i++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == 0x02014b50) return i;
        throw new InvalidDataException("Test fixture has no central directory.");
    }
}
