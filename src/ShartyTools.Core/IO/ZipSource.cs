using System.Buffers.Binary;
using System.IO.Compression;

namespace ShartyTools.Core.IO;

internal sealed record ZipEntryIdentity(long ArchiveLength, long DataOffset, long CompressedLength, int Method, uint Crc32);

/// <summary>Read-only ZIP indexing with bounded directory parsing and decompression. Never extracts files.</summary>
public static class ZipSource
{
    public const int MaxEntries = 16384;
    public const long MaxArchiveBytes = 2L * 1024 * 1024 * 1024;
    public const long MaxEntryBytes = 512L * 1024 * 1024;
    public const long MaxExpandedBytes = 8L * 1024 * 1024 * 1024;
    private const int MaxDirectoryBytes = 32 * 1024 * 1024;
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static IReadOnlyList<AssetFile> Read(string path)
    {
        GamePath.RejectLinks(path);
        using var input = File.OpenRead(path);
        var directory = ReadDirectory(input);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
        if (archive.Entries.Count != directory.Count) throw new InvalidDataException("ZIP entry count changed during indexing.");
        var files = new List<AssetFile>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var (entry, index) in archive.Entries.Select((entry, index) => (entry, index)))
        {
            ValidateEntry(entry);
            var isDirectory = entry.FullName.EndsWith('/');
            var name = ValidateRelativePath(isDirectory ? entry.FullName[..^1] : entry.FullName);
            if (!names.Add(name)) throw new InvalidDataException($"Duplicate ZIP entry: {name}");
            if (isDirectory) continue;
            total += entry.Length;
            if (total > MaxExpandedBytes) throw new InvalidDataException("ZIP exceeds the 8 GiB expanded content limit.");
            files.Add(new AssetFile(name, Path.GetFullPath(path), 0, entry.Length)
            {
                ZipEntry = new ZipEntryIdentity(input.Length, directory[index].DataOffset, entry.CompressedLength, directory[index].Method, entry.Crc32)
            });
        }
        return files;
    }

    // Inventory may include readmes with spaces/Unicode so they can be explicitly excluded.
    // Included output paths must additionally pass the stricter GamePath validation.
    public static string ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Any(c => char.IsControl(c) || c is '\\' or ':') ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Invalid source-relative path: '{path}'");
        return path;
    }

    internal static void ValidateEntry(ZipArchiveEntry entry)
    {
        var kind = (entry.ExternalAttributes >> 16) & 0xf000;
        if (kind is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"ZIP links and special files are unsupported: {entry.FullName}");
        if (entry.IsEncrypted) throw new InvalidDataException($"Encrypted ZIP entries are unsupported: {entry.FullName}");
        if (entry.Length < 0 || entry.Length > MaxEntryBytes || entry.CompressedLength < 0 || entry.CompressedLength > MaxArchiveBytes)
            throw new InvalidDataException($"ZIP entry exceeds the 512 MiB expanded file limit: {entry.FullName}");
        if ((entry.FullName.EndsWith('/') || kind == 0x4000) && (entry.Length != 0 || !entry.FullName.EndsWith('/')))
            throw new InvalidDataException($"Invalid ZIP directory entry: {entry.FullName}");
    }

    private sealed record Member(long LocalOffset, long DataOffset, int Method, ushort Flags, uint Crc32, uint CompressedLength, uint Length, byte[] Name);

    private static IReadOnlyList<Member> ReadDirectory(FileStream input)
    {
        // Bound the central directory BEFORE ZipArchive materializes its entries. The initial
        // entry-count field alone is insufficient: a malformed archive can under-report it.
        if (input.Length < 22 || input.Length > MaxArchiveBytes) throw new InvalidDataException("ZIP size must be between 22 bytes and 2 GiB.");
        var tail = new byte[(int)Math.Min(input.Length, 65557)];
        input.Position = input.Length - tail.Length;
        input.ReadExactly(tail);
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
            if (U32(tail.AsSpan(i)) == 0x06054b50 && i + 22 + U16(tail.AsSpan(i + 20)) == tail.Length) { end = i; break; }
        if (end < 0) throw new InvalidDataException("Missing ZIP end directory record.");
        var record = tail.AsSpan(end);
        var count = U16(record[10..]);
        var size = U32(record[12..]);
        var offset = U32(record[16..]);
        var endPosition = input.Length - tail.Length + end;
        if (U16(record[4..]) != 0 || U16(record[6..]) != 0 || U16(record[8..]) != count || count > MaxEntries ||
            size > MaxDirectoryBytes || (long)offset + size != endPosition)
            throw new InvalidDataException("Unsupported ZIP directory: requires a single-volume, non-ZIP64 archive with at most 16384 entries and a 32 MiB directory.");
        input.Position = offset;
        var members = new List<Member>();
        Span<byte> header = stackalloc byte[46];
        for (var i = 0; i < count; i++)
        {
            if (input.Position + header.Length > endPosition) throw new InvalidDataException("Truncated ZIP directory.");
            input.ReadExactly(header);
            if (U32(header) != 0x02014b50 || U16(header[34..]) != 0) throw new InvalidDataException("Invalid ZIP directory entry.");
            if (U16(header[10..]) is not (0 or 8)) throw new InvalidDataException("ZIP supports only stored or Deflate entries.");
            if (U32(header[20..]) == uint.MaxValue || U32(header[24..]) == uint.MaxValue || U32(header[42..]) == uint.MaxValue)
                throw new InvalidDataException("ZIP64 entries are unsupported.");
            var nameLength = U16(header[28..]);
            if (nameLength > 4096 || input.Position + nameLength > endPosition) throw new InvalidDataException("ZIP entry name exceeds its bounds.");
            var name = new byte[nameLength];
            input.ReadExactly(name);
            members.Add(new Member(U32(header[42..]), 0, U16(header[10..]), U16(header[8..]), U32(header[16..]), U32(header[20..]), U32(header[24..]), name));
            input.Position += U16(header[30..]) + U16(header[32..]);
            if (input.Position > endPosition) throw new InvalidDataException("ZIP directory entry exceeds its bounds.");
        }
        if (input.Position != endPosition) throw new InvalidDataException("ZIP directory entry count does not match its contents.");
        // Resolve and verify each local header once. Reopening the central directory for
        // every asset would turn a jam containing thousands of files into quadratic work.
        Span<byte> local = stackalloc byte[30];
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member.LocalOffset + local.Length > offset) throw new InvalidDataException("ZIP local header overlaps the central directory.");
            input.Position = member.LocalOffset;
            input.ReadExactly(local);
            if (U32(local) != 0x04034b50 || U16(local[6..]) != member.Flags || U16(local[8..]) != member.Method || U16(local[26..]) != member.Name.Length)
                throw new InvalidDataException("ZIP local header does not match its directory entry.");
            if ((member.Flags & 8) == 0 && (U32(local[14..]) != member.Crc32 || U32(local[18..]) != member.CompressedLength || U32(local[22..]) != member.Length))
                throw new InvalidDataException("ZIP local sizes or CRC do not match the central directory.");
            var dataOffset = member.LocalOffset + local.Length + member.Name.Length + U16(local[28..]);
            if (dataOffset + member.CompressedLength > offset || (member.Method == 0 && member.CompressedLength != member.Length))
                throw new InvalidDataException("ZIP compressed data exceeds its bounds.");
            var localName = new byte[member.Name.Length];
            input.ReadExactly(localName);
            if (!localName.AsSpan().SequenceEqual(member.Name)) throw new InvalidDataException("ZIP local and central names differ.");
            members[i] = member with { DataOffset = dataOffset };
        }
        var ranges = members.OrderBy(member => member.LocalOffset).ToArray();
        for (var i = 1; i < ranges.Length; i++)
            if (ranges[i - 1].DataOffset + ranges[i - 1].CompressedLength > ranges[i].LocalOffset)
                throw new InvalidDataException("ZIP entries overlap.");
        input.Position = 0;
        return members;
    }

    internal static Stream OpenEntry(FileStream input, ZipEntryIdentity identity)
    {
        if (input.Length != identity.ArchiveLength) throw new IOException("ZIP source changed after indexing; refresh the source and retry.");
        input.Position = identity.DataOffset;
        var range = new EntryRange(input, identity.CompressedLength);
        return identity.Method == 0 ? range : new DeflateStream(range, CompressionMode.Decompress);
    }

    private sealed class EntryRange(FileStream input, long length) : Stream
    {
        private long remaining = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get; } = length;
        public override long Position { get => Length - remaining; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = input.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            remaining -= count;
            return count;
        }
        // The AssetFile read operation owns the FileStream; disposing this range leaves it open.
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static ushort U16(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    private static uint U32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);

    internal static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320u);
            table[i] = value;
        }
        return table;
    }
}
