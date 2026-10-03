using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ShartyTools.Core.IO;

public sealed record AssetFile(string Name, string Source, long Offset, long Length)
{
    public byte[] ReadAll(int limit = 512 * 1024 * 1024)
    {
        if (Length > limit) throw new InvalidDataException($"{Name} exceeds the {limit / 1024 / 1024} MiB read limit.");
        var bytes = new byte[checked((int)Length)];
        using var input = Open();
        input.ReadExactly(bytes);
        return bytes;
    }

    private FileStream Open()
    {
        GamePath.RejectLinks(Source);
        var input = new FileStream(Source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length < Offset + Length)
        {
            input.Dispose();
            throw new IOException($"Source changed while being read: {Source}");
        }
        input.Position = Offset;
        return input;
    }

    public string CopyAndHash(Stream? output = null)
    {
        using var input = Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var remaining = Length;
        while (remaining > 0)
        {
            var count = input.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (count == 0) throw new EndOfStreamException($"Source was truncated: {Source}");
            hash.AppendData(buffer, 0, count);
            output?.Write(buffer, 0, count);
            remaining -= count;
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

public sealed class AssetCatalog
{
    public Dictionary<string, AssetFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Duplicates { get; } = [];

    public static AssetCatalog Create(IEnumerable<string> sources, bool referenceOnly = false)
    {
        var result = new AssetCatalog();
        foreach (var source in sources)
        {
            GamePath.RejectLinks(source);
            if (Directory.Exists(source))
            {
                foreach (var file in Walk(source))
                {
                    if (referenceOnly && Path.GetExtension(file).Equals(".pak", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var entry in ReadPak(file)) result.Add(entry, true);
                    }
                    else
                    {
                        var name = GamePath.Validate(Path.GetRelativePath(source, file).Replace('\\', '/'));
                        result.Add(new AssetFile(name, file, 0, new FileInfo(file).Length), referenceOnly);
                    }
                }
            }
            else if (File.Exists(source) && Path.GetExtension(source).Equals(".pak", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in ReadPak(source)) result.Add(entry, referenceOnly);
            }
            else throw new InvalidDataException($"Content source is not a folder or PAK: {source}");
        }
        return result;
    }

    private static IEnumerable<string> Walk(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Remove the link from the content tree before packaging: {entry}");
            if (Directory.Exists(entry))
            {
                foreach (var file in Walk(entry)) yield return file;
            }
            else yield return entry;
        }
    }

    private void Add(AssetFile file, bool referenceOnly)
    {
        if (Files.TryGetValue(file.Name, out var previous))
        {
            if (referenceOnly) { Files[file.Name] = file; return; }
            if (previous.Name != file.Name)
                throw new InvalidDataException($"Case collision: '{previous.Name}' and '{file.Name}'. Rename one before packaging.");
            if (previous.Length != file.Length || previous.CopyAndHash() != file.CopyAndHash())
                throw new InvalidDataException($"Asset conflict for '{file.Name}': {previous.Source} and {file.Source}. Resolve it before packaging.");
            Duplicates.Add(file.Name);
        }
        else Files.Add(file.Name, file);
    }

    public static IReadOnlyList<AssetFile> ReadPak(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[12];
        input.ReadExactly(header);
        if (!header[..4].SequenceEqual("PACK"u8)) throw new InvalidDataException($"Not a Quake PAK: {path}");
        var offset = BinaryPrimitives.ReadInt32LittleEndian(header[4..8]);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header[8..12]);
        if (offset < 12 || length < 0 || length % 64 != 0 || (long)offset + length > input.Length || length / 64 > 100000)
            throw new InvalidDataException("Invalid PAK directory bounds.");
        input.Position = offset;
        var entries = new List<AssetFile>();
        Span<byte> entry = stackalloc byte[64];
        for (var i = 0; i < length / 64; i++)
        {
            input.ReadExactly(entry);
            var end = entry[..56].IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("PAK entry name is not null terminated.");
            var name = GamePath.Validate(Encoding.Latin1.GetString(entry[..end]), 55);
            var fileOffset = BinaryPrimitives.ReadInt32LittleEndian(entry[56..60]);
            var fileLength = BinaryPrimitives.ReadInt32LittleEndian(entry[60..64]);
            if (fileOffset < 12 || fileLength < 0 || (long)fileOffset + fileLength > input.Length ||
                (fileLength > 0 && fileOffset < (long)offset + length && (long)fileOffset + fileLength > offset))
                throw new InvalidDataException($"PAK entry '{name}' has invalid bounds.");
            entries.Add(new AssetFile(name, Path.GetFullPath(path), fileOffset, fileLength));
        }
        var ranges = entries.Where(x => x.Length > 0).OrderBy(x => x.Offset).ToArray();
        for (var i = 1; i < ranges.Length; i++)
            if (ranges[i - 1].Offset + ranges[i - 1].Length > ranges[i].Offset)
                throw new InvalidDataException("PAK entries overlap.");
        return entries;
    }
}
