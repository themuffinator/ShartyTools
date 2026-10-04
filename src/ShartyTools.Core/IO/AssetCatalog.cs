using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ShartyTools.Core.IO;

public sealed record AssetFile(string Name, string Source, long Offset, long Length)
{
    internal ZipEntryIdentity? ZipEntry { get; init; }
    public AssetOrigin? Origin { get; init; }

    public byte[] ReadAll(int limit = 512 * 1024 * 1024)
    {
        if (Length > limit) throw new InvalidDataException($"{Name} exceeds the {limit / 1024 / 1024} MiB read limit.");
        var bytes = new byte[checked((int)Length)];
        using var output = new MemoryStream(bytes, true);
        ReadAndCopy(output, null);
        return bytes;
    }

    private void ReadAndCopy(Stream? output, IncrementalHash? hash)
    {
        GamePath.RejectLinks(Source);
        using var input = new FileStream(Source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (ZipEntry is { } identity)
        {
            using var content = ZipSource.OpenEntry(input, identity);
            Copy(content, output, hash, identity.Crc32);
            return;
        }
        if (input.Length < Offset + Length)
            throw new IOException($"Source changed while being read: {Source}");
        input.Position = Offset;
        Copy(input, output, hash, null);
    }

    public string CopyAndHash(Stream? output = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ReadAndCopy(output, hash);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private void Copy(Stream input, Stream? output, IncrementalHash? hash, uint? expectedCrc)
    {
        var buffer = new byte[81920];
        var remaining = Length;
        var crc = uint.MaxValue;
        while (remaining > 0)
        {
            var count = input.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (count == 0) throw new EndOfStreamException($"Source was truncated: {Source}");
            hash?.AppendData(buffer, 0, count);
            if (expectedCrc.HasValue) crc = ZipSource.UpdateCrc(crc, buffer.AsSpan(0, count));
            output?.Write(buffer, 0, count);
            remaining -= count;
        }
        if (expectedCrc.HasValue && (input.ReadByte() != -1 || ~crc != expectedCrc.Value))
            throw new InvalidDataException($"ZIP entry has an invalid expanded size or CRC: {Name}");
    }
}

public sealed record AssetOrigin(int SourceNumber, string Entry);

public sealed class AssetCatalog
{
    public Dictionary<string, AssetFile> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Duplicates { get; } = [];

    public static AssetCatalog Create(IEnumerable<string> sources, bool referenceOnly = false)
    {
        var result = new AssetCatalog();
        foreach (var source in sources)
        {
            foreach (var file in ReadSource(source, referenceOnly)) result.Add(file, referenceOnly);
        }
        return result;
    }

    public static AssetCatalog FromFiles(IEnumerable<AssetFile> files)
    {
        var result = new AssetCatalog();
        foreach (var file in files) result.Add(file, false);
        return result;
    }

    public static IReadOnlyList<AssetFile> ReadSource(string source, bool referenceOnly = false)
    {
        GamePath.RejectLinks(source);
        if (Directory.Exists(source))
        {
            var files = new List<AssetFile>();
            foreach (var file in Walk(source))
            {
                if (referenceOnly && Path.GetExtension(file).Equals(".pak", StringComparison.OrdinalIgnoreCase))
                    files.AddRange(ReadPak(file));
                else
                {
                    var name = ZipSource.ValidateRelativePath(Path.GetRelativePath(source, file).Replace('\\', '/'));
                    files.Add(new AssetFile(name, Path.GetFullPath(file), 0, new FileInfo(file).Length));
                }
            }
            return files;
        }
        if (File.Exists(source))
        {
            if (Path.GetExtension(source).Equals(".pak", StringComparison.OrdinalIgnoreCase)) return ReadPak(source);
            if (Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return ZipSource.Read(source);
        }
        throw new InvalidDataException($"Content source is not a folder, PAK or ZIP: {source}");
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
        GamePath.Validate(file.Name);
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
