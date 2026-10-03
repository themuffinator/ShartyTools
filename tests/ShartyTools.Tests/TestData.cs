using System.Buffers.Binary;
using System.Text;

namespace ShartyTools.Tests;

internal static class TestData
{
    public static byte[] Md2(int skinCount = 1, int frames = 1)
    {
        var skinEnd = 68 + skinCount * 64;
        var end = skinEnd + 8 + 12 + 12 + 52 * frames + 4;
        var bytes = new byte[end + 7]; // Preserve section padding and a vendor trailer.
        int[] header = [0x32504449, 8, 64, 64, 52, skinCount, 3, 3, 1, 1, frames, 68, skinEnd + 8, skinEnd + 20, skinEnd + 32, end - 4, end];
        for (var i = 0; i < header.Length; i++) Int(bytes, i * 4, header[i]);
        for (var i = 0; i < skinCount; i++) Encoding.ASCII.GetBytes($"models/test/skin{i}.pcx").CopyTo(bytes, 68 + i * 64);
        for (var i = skinEnd; i < bytes.Length; i++) bytes[i] = (byte)(i % 251);
        Int(bytes, end - 4, 0); // GL-command terminator.
        return bytes;
    }

    public const string Entities = """
        {
        "classname" "worldspawn"
        "message" "The Test Chamber"
        "author" "Test Mapper"
        }
        { "classname" "info_player_start" }
        { "classname" "info_player_deathmatch" }
        { "classname" "info_player_coop" }
        """;

    public static byte[] Bsp(string entities = Entities, string format = "IBSP", string? texture = null)
    {
        var text = Encoding.Latin1.GetBytes(entities + "\0");
        var bytes = new byte[160 + text.Length + (texture is null ? 0 : 76)];
        Encoding.ASCII.GetBytes(format).CopyTo(bytes, 0);
        Int(bytes, 4, 38);
        Int(bytes, 8, 160);
        Int(bytes, 12, text.Length);
        text.CopyTo(bytes, 160);
        if (texture is not null)
        {
            Int(bytes, 48, 160 + text.Length);
            Int(bytes, 52, 76);
            Encoding.ASCII.GetBytes(texture).CopyTo(bytes, 160 + text.Length + 40);
        }
        return bytes;
    }

    public static void Int(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    public static int Int(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
}

internal sealed class TestFolder : IDisposable
{
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ShartyTools.slnx"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Run tests from a repository build.");
        }
    }
    public string Root { get; } = Path.Combine(RepositoryRoot, ".agents", "tmp", "tests", Guid.NewGuid().ToString("N"));
    public TestFolder() => Directory.CreateDirectory(Root);
    public string PathFor(string relative) => Path.Combine(Root, relative);
    public string Write(string relative, byte[] bytes)
    {
        var path = PathFor(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }
    public string WriteText(string relative, string text) => Write(relative, Encoding.UTF8.GetBytes(text));
    public void Dispose()
    {
        var parent = Path.GetFullPath(Path.Combine(RepositoryRoot, ".agents", "tmp", "tests"));
        if (!Root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid cleanup root.");
        ShartyTools.Core.IO.GamePath.RejectLinks(Root);
        // Never recurse through links, including links deliberately made by a test.
        foreach (var entry in Directory.EnumerateFileSystemEntries(Root)) Delete(entry);
        Directory.Delete(Root);
    }
    private static void Delete(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(path);
            else File.Delete(path);
        }
        else if ((attributes & FileAttributes.Directory) != 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path)) Delete(entry);
            Directory.Delete(path);
        }
        else File.Delete(path);
    }
}
