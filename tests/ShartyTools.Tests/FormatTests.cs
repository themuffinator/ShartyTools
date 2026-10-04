using ShartyTools.Core.Formats;
using ShartyTools.Core.IO;

namespace ShartyTools.Tests;

public sealed class FormatTests
{
    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    public void Md2EditsPreserveEveryByteOutsideHeaderAndSkinTable(int oldCount, int newCount)
    {
        var input = TestData.Md2(oldCount);
        var model = Md2Model.Read(input);
        var skins = Enumerable.Range(0, newCount).Select(i => $"models/test/new{i}.pcx").ToArray();
        var output = model.WithSkins(skins);
        Assert.Equal(skins, Md2Model.Read(output).Skins);
        Assert.Equal(input.AsSpan(68 + oldCount * 64).ToArray(), output.AsSpan(68 + newCount * 64).ToArray());
        Assert.Equal(input.AsSpan(0, 20).ToArray(), output.AsSpan(0, 20).ToArray());
        Assert.Equal(input.AsSpan(24, 24).ToArray(), output.AsSpan(24, 24).ToArray());
        for (var i = 12; i < 17; i++) Assert.Equal(TestData.Int(input, i * 4) + (newCount - oldCount) * 64, TestData.Int(output, i * 4));
    }

    [Theory]
    [InlineData("../evil.pcx")]
    [InlineData("C:/skin.pcx")]
    [InlineData("/skin.pcx")]
    [InlineData("skin\\bad.pcx")]
    [InlineData("skin/é.pcx")]
    [InlineData("")]
    public void Md2RejectsUnsafeSkinPaths(string path) => Assert.Throws<InvalidDataException>(() => Md2Model.Read(TestData.Md2()).WithSkins([path]));

    [Fact]
    public void Md2EnforcesNameLengthAndClassicSkinLimit()
    {
        var model = Md2Model.Read(TestData.Md2());
        _ = model.WithSkins([new string('a', 63)]);
        Assert.Throws<InvalidDataException>(() => model.WithSkins([new string('a', 64)]));
        Assert.Throws<InvalidDataException>(() => model.WithSkins(Enumerable.Range(0, 33).Select(i => $"skin{i}.pcx")));
        Assert.Throws<InvalidDataException>(() => model.WithSkins(["skin.pcx", "SKIN.pcx"]));
    }

    [Theory]
    [InlineData(4, 7)]
    [InlineData(20, -1)]
    [InlineData(44, 0)]
    [InlineData(48, 68)]
    [InlineData(64, int.MaxValue)]
    public void Md2RejectsMalformedHeaders(int offset, int value)
    {
        var bytes = TestData.Md2();
        TestData.Int(bytes, offset, value);
        Assert.Throws<InvalidDataException>(() => Md2Model.Read(bytes));
    }

    [Fact]
    public void Md2WillNotOverwriteOriginalOrExistingOutput()
    {
        using var folder = new TestFolder();
        var source = folder.Write("model.md2", TestData.Md2());
        var model = Md2Model.Load(source);
        Assert.Throws<IOException>(() => model.SaveCopy(source, source, ["skin.pcx"]));
        var copy = folder.WriteText("copy.md2", "do not touch");
        Assert.Throws<IOException>(() => model.SaveCopy(source, copy, ["skin.pcx"]));
        Assert.Equal("do not touch", File.ReadAllText(copy));
        Assert.Equal(TestData.Md2(), File.ReadAllBytes(source));
    }

    [Theory]
    [InlineData("IBSP")]
    [InlineData("QBSP")]
    public void BspReadsEntitiesAndTextureReferences(string format)
    {
        var bsp = BspFile.Read(TestData.Bsp(format: format, texture: "test/wall"));
        Assert.Equal(format, bsp.Format);
        Assert.Equal("The Test Chamber", bsp.Entities[0]["message"]);
        Assert.Equal(["test/wall"], bsp.Textures);
        Assert.Equal(4, bsp.Entities.Count);
    }

    [Fact]
    public void BspRejectsOutOfBoundsAndOverlappingLumps()
    {
        var bytes = TestData.Bsp();
        TestData.Int(bytes, 12, int.MaxValue);
        Assert.Throws<InvalidDataException>(() => BspFile.Read(bytes));
        bytes = TestData.Bsp();
        TestData.Int(bytes, 16, 160);
        TestData.Int(bytes, 20, 4);
        Assert.Throws<InvalidDataException>(() => BspFile.Read(bytes));
    }

    [Fact]
    public void EntityParserPreservesLiteralBackslashesAndComments()
    {
        var entities = EntityText.Parse("// header\n { \"message\" \"line\\ntext\" // comment\n \"classname\" \"worldspawn\" }");
        Assert.Equal("line\\ntext", entities[0]["message"]);
        Assert.Equal(entities[0]["message"], EntityText.Parse(EntityText.Serialize(entities))[0]["message"]);
    }

    [Theory]
    [InlineData("{ \"key\" }")]
    [InlineData("{ \"key\" \"value")]
    [InlineData("{ { }")]
    public void EntityParserRejectsTruncatedOrAmbiguousEntities(string source) => Assert.Throws<InvalidDataException>(() => EntityText.Parse(source));

    [Fact]
    public void RepeatedEntityKeysMatchGameLastValueBehavior()
    {
        var entity = EntityText.Parse("{ \"light\" \"100\" \"light\" \"200\" }")[0];
        Assert.Equal("200", entity["light"]);
        Assert.Equal(["light"], entity.RepeatedKeys);
    }

    [Fact]
    public void StockRereleaseFrameCountsAreSupportedAndMarkedExtended()
    {
        var model = Md2Model.Read(TestData.Md2(frames: 799));
        Assert.False(model.ClassicCompatible);
        Assert.Equal(799, Md2Model.Read(model.WithSkins(["models/test/skin.pcx"])).FrameCount);
        Assert.Throws<InvalidDataException>(() => Md2Model.Read(TestData.Md2(frames: 1025)));
    }

    [Fact]
    public void FailedAtomicWritePreservesExistingFileAndRemovesTemporary()
    {
        using var folder = new TestFolder();
        var path = folder.WriteText("keep.txt", "original");
        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(path, stream => { stream.WriteByte(42); throw new InvalidOperationException(); }, true));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(folder.Root));
    }
}
