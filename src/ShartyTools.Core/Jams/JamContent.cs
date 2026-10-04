using System.Text;
using ShartyTools.Core.IO;

namespace ShartyTools.Core.Jams;

public sealed record ContentFile(int SourceNumber, string Source, string Path, string? GamePath, bool Included, AssetFile Asset);

public static class JamContent
{
    public static IReadOnlyList<ContentFile> Inspect(JamProject project, string projectPath)
    {
        project.ValidateStructure();
        var files = new List<ContentFile>();
        var sources = project.ResolveSources(projectPath);
        for (var i = 0; i < sources.Length; i++)
        {
            var source = project.Sources[i];
            var settings = project.SourceSettings.GetValueOrDefault(source) ?? new SourceSettings();
            var excluded = settings.ExcludedFiles.ToHashSet(StringComparer.Ordinal);
            var entries = AssetCatalog.ReadSource(sources[i]);
            var prefix = settings.Root.Length == 0 ? "" : settings.Root + "/";
            if (prefix.Length > 0 && !entries.Any(entry => entry.Name.StartsWith(prefix, StringComparison.Ordinal)))
                throw new InvalidDataException($"Content root '{settings.Root}' does not contain any files in source #{i + 1}: {source}");
            foreach (var entry in entries.OrderBy(file => file.Name, StringComparer.Ordinal))
            {
                var gamePath = entry.Name.StartsWith(prefix, StringComparison.Ordinal) ? entry.Name[prefix.Length..] : null;
                files.Add(new ContentFile(i + 1, source, entry.Name, gamePath, gamePath is not null && !excluded.Contains(entry.Name), entry));
            }
        }
        return files;
    }

    public static AssetCatalog CreateCatalog(JamProject project, string projectPath) => AssetCatalog.FromFiles(
        Inspect(project, projectPath).Where(file => file.Included).Select(file => file.Asset with
        {
            Name = file.GamePath!, Origin = new AssetOrigin(file.SourceNumber, file.Path)
        }));

    public static string SourceAt(JamProject project, string number) => int.TryParse(number, out var index) && index > 0 && index <= project.Sources.Count
        ? project.Sources[index - 1] : throw new ArgumentException("Source number must match the 1-based number from 'jam files'.");

    public static void AddSource(JamProject project, string projectPath, string sourcePath, string root = "")
    {
        var absolute = System.IO.Path.GetFullPath(sourcePath);
        var resolved = project.ResolveSources(projectPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (resolved.Any(source => string.Equals(source, absolute, comparison))) throw new InvalidDataException("That content source is already present.");
        if (root.Length > 0) ZipSource.ValidateRelativePath(root);
        var entries = AssetCatalog.ReadSource(absolute);
        if (root.Length > 0 && !entries.Any(entry => entry.Name.StartsWith(root + "/", StringComparison.Ordinal)))
            throw new InvalidDataException($"Content root '{root}' does not contain any files in this source.");
        var relative = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath))!, absolute).Replace('\\', '/');
        project.Sources.Add(relative);
        project.SchemaVersion = JamProject.CurrentSchemaVersion;
        if (root.Length > 0) Settings(project, relative).Root = root;
    }

    public static void SetRoot(JamProject project, string source, string root)
    {
        if (root.Length > 0) ZipSource.ValidateRelativePath(root);
        Settings(project, source).Root = root;
    }

    public static void SetIncluded(JamProject project, string source, string path, bool included)
    {
        ZipSource.ValidateRelativePath(path);
        var settings = Settings(project, source);
        settings.ExcludedFiles.RemoveAll(value => value == path);
        if (!included) settings.ExcludedFiles.Add(path);
        settings.ExcludedFiles.Sort(StringComparer.Ordinal);
    }

    public static void ImportMapDb(JamProject project, string projectPath, string source, string path)
    {
        var file = Inspect(project, projectPath).SingleOrDefault(file => file.Source == source && file.Path == path)
            ?? throw new FileNotFoundException("The selected mapdb was not found in the content source.");
        if (!string.Equals(file.GamePath, "mapdb.json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Select mapdb.json at the chosen content root. Adjust the source root if necessary.");
        var json = new UTF8Encoding(false, true).GetString(file.Asset.ReadAll(4 * 1024 * 1024)).TrimStart('\uFEFF');
        MapDatabase.Import(project, json);
        SetIncluded(project, source, path, false);
    }

    private static SourceSettings Settings(JamProject project, string source)
    {
        if (!project.Sources.Contains(source, StringComparer.Ordinal)) throw new ArgumentException("Unknown content source.");
        project.SchemaVersion = JamProject.CurrentSchemaVersion;
        if (!project.SourceSettings.TryGetValue(source, out var settings)) project.SourceSettings[source] = settings = new SourceSettings();
        return settings;
    }
}
