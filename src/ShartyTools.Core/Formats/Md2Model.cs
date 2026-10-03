using System.Buffers.Binary;
using System.Text;
using ShartyTools.Core.IO;

namespace ShartyTools.Core.Formats;

public sealed class Md2Model
{
    private readonly byte[] data;
    private readonly int[] header;
    public IReadOnlyList<string> Skins { get; }
    public int Width => header[2];
    public int Height => header[3];
    public int VertexCount => header[6];
    public int TriangleCount => header[8];
    public int FrameCount => header[10];
    public bool ClassicCompatible => FrameCount <= 512;
    public const int MaxSkins = 32;

    private Md2Model(byte[] data, int[] header, IReadOnlyList<string> skins)
    {
        this.data = data;
        this.header = header;
        Skins = skins;
    }

    public static Md2Model Load(string path) => Read(File.ReadAllBytes(path));

    public static Md2Model Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 68 || !bytes[..4].SequenceEqual("IDP2"u8))
            throw new InvalidDataException("Not an MD2 model (expected IDP2).");
        var h = new int[17];
        for (var i = 0; i < h.Length; i++) h[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(i * 4, 4));
        if (h[1] != 8) throw new InvalidDataException($"Unsupported MD2 version {h[1]}; expected 8.");
        if (h[2] <= 0 || h[3] <= 0 || h[5] is < 0 or > MaxSkins || h[6] is < 1 or > 2048 ||
            h[7] is < 1 or > 65536 || h[8] is < 1 or > 4096 || h[9] < 0 || h[10] is < 1 or > 1024 ||
            h[4] < 40L + h[6] * 4L || h[16] < 68 || h[16] > bytes.Length)
            throw new InvalidDataException("Invalid MD2 dimensions, counts, frame size or end offset (maximum 1024 frames, 2048 vertices and 4096 triangles).");
        long[] sizes = [h[5] * 64L, h[7] * 4L, h[8] * 12L, h[10] * (long)h[4], h[9] * 4L];
        for (var i = 0; i < sizes.Length; i++)
        {
            if (h[11 + i] < 68 || h[11 + i] + sizes[i] > h[16] ||
                (i < 4 && h[11 + i] + sizes[i] > h[12 + i]))
                throw new InvalidDataException("MD2 sections overlap, are out of order, or extend outside the model.");
        }
        var skins = new List<string>();
        for (var i = 0; i < h[5]; i++)
        {
            var skin = bytes.Slice(h[11] + i * 64, 64);
            var end = skin.IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("MD2 skin path is not null terminated.");
            if (skin[..end].ContainsAnyInRange((byte)128, byte.MaxValue))
                throw new InvalidDataException("MD2 skin paths must be ASCII.");
            // Empty existing slots occur in real models and remain visible/editable.
            skins.Add(Encoding.ASCII.GetString(skin[..end]));
        }
        return new Md2Model(bytes.ToArray(), h, skins.AsReadOnly());
    }

    public byte[] WithSkins(IEnumerable<string> paths)
    {
        var skins = paths.ToArray();
        if (skins.Length is < 1 or > MaxSkins)
            throw new InvalidDataException($"Use 1–{MaxSkins} skins for classic Quake II compatibility.");
        foreach (var skin in skins) GamePath.Validate(skin, 63);
        if (skins.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skins.Length)
            throw new InvalidDataException("The skin table contains duplicate paths.");
        var oldEnd = header[11] + header[5] * 64;
        var delta = (skins.Length - header[5]) * 64;
        var output = new byte[checked(data.Length + delta)];
        data.AsSpan(0, header[11]).CopyTo(output);
        data.AsSpan(oldEnd).CopyTo(output.AsSpan(oldEnd + delta));
        for (var i = 0; i < skins.Length; i++)
            Encoding.ASCII.GetBytes(skins[i], output.AsSpan(header[11] + i * 64, 63));
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(20, 4), skins.Length);
        for (var i = 12; i < 17; i++)
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(i * 4, 4), checked(header[i] + delta));
        _ = Read(output);
        return output;
    }

    public void SaveCopy(string inputPath, string outputPath, IEnumerable<string> paths)
    {
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Save the edited model to a different file; the original is kept intact.");
        var edited = WithSkins(paths);
        AtomicFile.Write(outputPath, stream => stream.Write(edited));
    }
}
