using System.Buffers.Binary;
using System.Text;

namespace ShartyTools.Core.Formats;

public sealed record BspFile(string Format, string EntitySource, IReadOnlyList<Entity> Entities, IReadOnlyList<string> Textures)
{
    public static BspFile Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 160) throw new InvalidDataException("Truncated Quake II BSP header.");
        var magic = Encoding.ASCII.GetString(bytes[..4]);
        if (magic is not ("IBSP" or "QBSP") || BinaryPrimitives.ReadInt32LittleEndian(bytes[4..8]) != 38)
            throw new InvalidDataException("Supported maps are Quake II IBSP 38 and extended QBSP 38.");
        var lumps = new (int Offset, int Length)[19];
        for (var i = 0; i < lumps.Length; i++)
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8 + i * 8, 4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(12 + i * 8, 4));
            if (length < 0 || offset < 0 || (long)offset + length > bytes.Length || (length > 0 && offset < 160))
                throw new InvalidDataException($"BSP lump {i} is outside the file.");
            lumps[i] = (offset, length);
        }
        var used = lumps.Where(x => x.Length > 0).OrderBy(x => x.Offset).ToArray();
        for (var i = 1; i < used.Length; i++)
            if ((long)used[i - 1].Offset + used[i - 1].Length > used[i].Offset)
                throw new InvalidDataException("BSP lumps overlap.");
        var ent = lumps[0];
        if (ent.Length > 16 * 1024 * 1024) throw new InvalidDataException("Entity lump exceeds the 16 MiB inspection limit.");
        var source = Encoding.Latin1.GetString(bytes.Slice(ent.Offset, ent.Length)).TrimEnd('\0');
        var tex = lumps[5];
        if (tex.Length % 76 != 0) throw new InvalidDataException("Invalid Quake II texinfo lump size.");
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tex.Length; i += 76)
        {
            var name = bytes.Slice(tex.Offset + i + 40, 32);
            var end = name.IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("BSP texture name is not null terminated.");
            var texture = Encoding.Latin1.GetString(name[..end]);
            if (texture.Length > 0) textures.Add(texture);
        }
        return new BspFile(magic, source, EntityText.Parse(source), textures.Order(StringComparer.Ordinal).ToArray());
    }

    public static BspFile Load(string path) => Read(File.ReadAllBytes(path));
}
