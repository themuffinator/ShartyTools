using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShartyTools.Core.IO;

namespace ShartyTools.Core.Jams;

public sealed record BuildResult(string Path, string Sha256, long Bytes, ValidationReport Report);

public static class JamBuilder
{
    private sealed record Payload(string Name, long Length, string Sha256, Action<Stream> Copy);

    public static BuildResult Build(JamProject project, string projectPath, string outputPath, bool strict = false)
    {
        outputPath = Path.GetFullPath(outputPath);
        GamePath.RejectLinks(outputPath);
        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not (".zip" or ".pak")) throw new InvalidDataException("Choose a .zip or .pak output filename.");
        foreach (var source in project.ResolveSources(projectPath).Concat(project.ResolveSources(projectPath, true)))
            if (GamePath.IsInside(outputPath, source))
                throw new InvalidDataException("Save builds outside content and reference roots to prevent packaging previous builds or modifying game assets.");
        var report = JamValidator.Validate(project, projectPath);
        if (report.HasErrors || (strict && report.Findings.Any(f => f.Severity == "warning")))
            throw new InvalidDataException("Build blocked by validation.\n" + report);
        var content = JamContent.CreateCatalog(project, projectPath);
        var payload = new List<Payload>();
        foreach (var asset in content.Files.Values)
        {
            var digest = asset.CopyAndHash();
            payload.Add(new Payload(asset.Name, asset.Length, digest, output =>
            {
                if (asset.CopyAndHash(output) != digest) throw new IOException($"'{asset.Name}' changed during packaging. Run the build again.");
            }));
        }
        void AddText(string name, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            payload.Add(new Payload(name, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), output => output.Write(bytes)));
        }
        if (project.Profile == "quake2-rerelease") AddText("mapdb.json", MapDatabase.Export(project));
        var start = string.IsNullOrEmpty(project.StartMap) ? project.Maps[0].Bsp : project.StartMap;
        AddText("sharty-credits.txt", $"{project.Title}\n\n" +
            string.Join('\n', project.Maps.Select(m => $"{m.Bsp}: {m.Title} — {(m.Author.Length == 0 ? "credit pending" : m.Author)}")) +
            $"\n\nInstall into the {project.Id}/ mod folder. Launch Quake II with +set game {project.Id} +map {start}.\n" +
            "For a PAK build, place the PAK in that mod folder. Keep all original asset licenses and credits.\n" +
            "Assembled with ShartyTools. Community reference: https://map-center.com/forums/news.2/\n");
        var manifest = new
        {
            schemaVersion = 2, jam = project.Id, title = project.Title, profile = project.Profile,
            sources = project.Sources.Select((source, index) => new
            {
                number = index + 1, name = Path.GetFileName(source.TrimEnd('/', '\\')),
                root = project.SourceSettings.GetValueOrDefault(source)?.Root ?? ""
            }).ToArray(),
            files = payload.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new
            {
                path = p.Name, bytes = p.Length, sha256 = p.Sha256,
                origin = content.Files.GetValueOrDefault(p.Name)?.Origin
            }).ToArray(),
            findings = report.Findings
        };
        AddText("sharty-manifest.json", JsonSerializer.Serialize(manifest, JamProject.JsonOptions) + "\n");
        payload = payload.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        if (extension == ".pak")
        {
            foreach (var item in payload) GamePath.Validate(item.Name, 55);
            if (payload.Sum(p => p.Length) + 12L + payload.Count * 64L > int.MaxValue)
                throw new InvalidDataException("PAK exceeds the signed 32-bit format limit; use ZIP.");
            if (project.Profile == "quake2-classic" && payload.Count > 4096)
                throw new InvalidDataException("Stock Quake II supports at most 4096 PAK entries; use ZIP or split the content.");
        }
        AtomicFile.Write(outputPath, output =>
        {
            if (extension == ".zip")
            {
                using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
                foreach (var item in payload)
                {
                    var entry = zip.CreateEntry($"{project.Id}/{item.Name}", CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    entry.ExternalAttributes = 0;
                    using var destination = entry.Open();
                    item.Copy(destination);
                }
            }
            else WritePak(output, payload);
        });
        using var package = File.OpenRead(outputPath);
        return new BuildResult(outputPath, Convert.ToHexStringLower(SHA256.HashData(package)), package.Length, report);
    }

    private static void WritePak(Stream output, IReadOnlyList<Payload> payload)
    {
        var offset = checked((int)(12 + payload.Sum(p => p.Length)));
        Span<byte> header = stackalloc byte[12];
        "PACK"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], offset);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], payload.Count * 64);
        output.Write(header);
        foreach (var item in payload) item.Copy(output);
        var position = 12;
        Span<byte> entry = stackalloc byte[64];
        foreach (var item in payload)
        {
            entry.Clear();
            Encoding.ASCII.GetBytes(item.Name, entry[..55]);
            BinaryPrimitives.WriteInt32LittleEndian(entry[56..60], position);
            BinaryPrimitives.WriteInt32LittleEndian(entry[60..64], checked((int)item.Length));
            output.Write(entry);
            position = checked(position + (int)item.Length);
        }
    }
}
