using System.Reflection;
using System.Text.Json;
using ShartyTools.Core.Formats;
using ShartyTools.Core.IO;
using ShartyTools.Core.Jams;

namespace ShartyTools.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine("Error: " + error.Message);
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            Console.WriteLine("""
                ShartyTools — Quake II jam assembly and MD2 skin editing

                sharty --version
                sharty jam new PROJECT ID TITLE
                sharty jam scan PROJECT
                sharty jam check PROJECT [--json] [--strict]
                sharty jam build PROJECT OUTPUT.zip|OUTPUT.pak [--strict]
                sharty mapdb import PROJECT INPUT.json
                sharty mapdb export PROJECT OUTPUT.json
                sharty bsp entities INPUT.bsp OUTPUT.ent
                sharty md2 inspect INPUT.md2
                sharty md2 skins INPUT.md2 OUTPUT.md2 SKIN [SKIN...]

                Paths with spaces must be quoted. Edit sources, referenceSources and map
                metadata in the desktop app or project JSON. Scan saves discovered maps.
                'md2 skins' replaces the full skin table in the supplied order; retain
                existing names to append. Output files must not already exist.
                Exit: 0 success; 1 failed QA (warnings with --strict); 2 invalid input/I/O.
                GPL-2.0-only. No warranty. See LICENSE in the release archive.
                """);
            return 0;
        }
        if (args is ["--version"])
        {
            Console.WriteLine(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            return 0;
        }
        if (args is ["jam", "new", var newPath, var id, var title])
        {
            newPath = Path.GetFullPath(newPath);
            var project = new JamProject { Id = id, Title = title };
            project.ValidateStructure();
            project.Save(newPath);
            var content = Path.Combine(Path.GetDirectoryName(newPath)!, "content");
            GamePath.RejectLinks(content);
            Directory.CreateDirectory(content);
            Console.WriteLine($"Created {newPath}. Put submissions in content/ or edit sources, then scan.");
            return 0;
        }
        if (args is ["jam", "scan", var scanPath])
        {
            var project = JamProject.Load(scanPath);
            var added = JamValidator.DiscoverMaps(project, scanPath);
            project.Save(scanPath, true);
            Console.WriteLine($"Added {added} maps; saved {scanPath}.");
            return 0;
        }
        if (args.Length >= 3 && args[0] == "jam" && args[1] == "check")
        {
            var flags = args.Skip(3).ToArray();
            if (flags.Any(f => f is not ("--json" or "--strict"))) throw new ArgumentException("Unknown check option.");
            var report = JamValidator.Validate(JamProject.Load(args[2]), args[2]);
            Console.WriteLine(flags.Contains("--json") ? JsonSerializer.Serialize(report, JamProject.JsonOptions) : report.ToString());
            return report.HasErrors || (flags.Contains("--strict") && report.Findings.Any(f => f.Severity == "warning")) ? 1 : 0;
        }
        if (args.Length is 4 or 5 && args[0] == "jam" && args[1] == "build")
        {
            if (args.Length == 5 && args[4] != "--strict") throw new ArgumentException("Unknown build option.");
            var result = JamBuilder.Build(JamProject.Load(args[2]), args[2], args[3], args.Length == 5);
            Console.WriteLine(result.Report);
            Console.WriteLine($"\nBuilt {result.Path}\nSHA-256 {result.Sha256}");
            return 0;
        }
        if (args is ["mapdb", "import", var projectPath, var jsonPath])
        {
            var project = JamProject.Load(projectPath);
            MapDatabase.Import(project, File.ReadAllText(jsonPath));
            project.Save(projectPath, true);
            Console.WriteLine($"Imported {project.Maps.Count} map entries; source paths are unchanged.");
            return 0;
        }
        if (args is ["mapdb", "export", var exportProject, var exportPath])
        {
            AtomicFile.WriteText(exportPath, MapDatabase.Export(JamProject.Load(exportProject)));
            Console.WriteLine($"Exported {exportPath}.");
            return 0;
        }
        if (args is ["bsp", "entities", var bspPath, var entPath])
        {
            AtomicFile.WriteText(entPath, BspFile.Load(bspPath).EntitySource + "\n");
            Console.WriteLine($"Extracted {entPath}.");
            return 0;
        }
        if (args is ["md2", "inspect", var modelPath])
        {
            var model = Md2Model.Load(modelPath);
            Console.WriteLine(JsonSerializer.Serialize(new { model.Width, model.Height, model.VertexCount, model.TriangleCount, model.FrameCount, model.ClassicCompatible, model.Skins }, JamProject.JsonOptions));
            return 0;
        }
        if (args.Length >= 5 && args[0] == "md2" && args[1] == "skins")
        {
            Md2Model.Load(args[2]).SaveCopy(args[2], args[3], args.Skip(4));
            Console.WriteLine($"Saved {args[3]}; original model unchanged.");
            return 0;
        }
        throw new ArgumentException("Unknown command or wrong number of arguments. Run sharty --help.");
    }
}
