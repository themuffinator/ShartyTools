using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ShartyTools.Core.Formats;
using ShartyTools.Core.Jams;

namespace ShartyTools.Desktop;

public sealed class WorkspaceViewModel : INotifyPropertyChanged
{
    private JamProject project = new();
    private JamMap? selectedMap;
    private string projectPath = "";
    private string sourcesText = "content";
    private string referenceText = "";
    private string mapDbText = "";
    private string reportText = "Run checks to inspect spawn points, target links, map transitions and asset references.";
    private string entityText = "Select a map and choose Inspect entities to read its compiled entity lump.";
    private string status = "Ready. Create a jam project or open an MD2 model to get started.";
    private string skinsText = "";
    private string modelSummary = "Open an MD2 model to inspect its skin table.";
    private bool isBusy;
    private string savedJson = "";
    private string savedSkins = "";
    private string mapDbBaseline = "";
    private IReadOnlyList<ContentFile> inventory = [];
    private string? selectedSource;
    private SourceFileRow? selectedFile;
    private string fileFilter = "";

    public WorkspaceViewModel() => savedJson = Fingerprint();
    public event PropertyChangedEventHandler? PropertyChanged;
    public JamProject Project { get => project; private set => Set(ref project, value); }
    public ObservableCollection<JamMap> Maps { get; } = [];
    public JamMap? SelectedMap { get => selectedMap; set { Set(ref selectedMap, value); Notify(nameof(HasSelectedMap)); } }
    public bool HasSelectedMap => SelectedMap is not null;
    public string ProjectPath { get => projectPath; private set { Set(ref projectPath, value); Notify(nameof(ProjectLocation)); } }
    public string ProjectLocation => ProjectPath.Length == 0 ? "Unsaved project" : ProjectPath;
    public string SourcesText { get => sourcesText; set => Set(ref sourcesText, value); }
    public string ReferenceText { get => referenceText; set => Set(ref referenceText, value); }
    public string MapDbText { get => mapDbText; set => Set(ref mapDbText, value); }
    public string ReportText { get => reportText; set => Set(ref reportText, value); }
    public string EntityText { get => entityText; set => Set(ref entityText, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string SkinsText { get => skinsText; set => Set(ref skinsText, value); }
    public string ModelSummary { get => modelSummary; private set => Set(ref modelSummary, value); }
    public bool IsBusy { get => isBusy; set { Set(ref isBusy, value); Notify(nameof(IsIdle)); } }
    public bool IsIdle => !IsBusy;
    public string Version => typeof(WorkspaceViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
    public Md2Model? Model { get; private set; }
    public string ModelPath { get; private set; } = "";
    public string EntityMap { get; set; } = "";
    public bool MapDbDirty => MapDbText != mapDbBaseline;
    public bool JamDirty => Fingerprint() != savedJson || MapDbDirty;
    public bool SkinsDirty => SkinsText != savedSkins;
    public ObservableCollection<string> ContentSources { get; } = [];
    public ObservableCollection<SourceFileRow> SourceFiles { get; } = [];
    public string? SelectedSource
    {
        get => selectedSource;
        set { Set(ref selectedSource, value); RefreshFileRows(); Notify(nameof(SourceRootLabel)); }
    }
    public SourceFileRow? SelectedFile
    {
        get => selectedFile;
        set { Set(ref selectedFile, value); Notify(nameof(FileDetails)); Notify(nameof(CanSelectFile)); Notify(nameof(CanImportMapDb)); }
    }
    public string FileFilter { get => fileFilter; set { Set(ref fileFilter, value); RefreshFileRows(); } }
    public bool CanSelectFile => SelectedFile?.File.GamePath is not null;
    public bool CanImportMapDb => string.Equals(SelectedFile?.File.GamePath, "mapdb.json", StringComparison.OrdinalIgnoreCase);
    public string SourceRootLabel => "Content root: " + (SelectedSource is { } source && Project.SourceSettings.GetValueOrDefault(source)?.Root is { Length: > 0 } root ? root : "source root");
    public string FileSummary => inventory.Count == 0 ? "Refresh files to review submissions." :
        $"{inventory.Count(file => file.Included):N0} included / {inventory.Count:N0} files across {ContentSources.Count} sources · {inventory.Where(file => file.Included).Sum(file => file.Asset.Length) / 1024.0 / 1024.0:F1} MiB";
    public string FileDetails
    {
        get
        {
            if (SelectedFile is not { } row) return "Select a file to review its source and package path. Exclusions leave the original submission intact.";
            var file = row.File;
            var copies = inventory.Where(other => other != file && other.GamePath is not null && string.Equals(other.GamePath, file.GamePath, StringComparison.OrdinalIgnoreCase))
                .Select(other => $"#{other.SourceNumber} {other.Source} ({(other.Included ? "included" : "excluded")})");
            return $"Source #{file.SourceNumber}: {file.Source}\nOriginal path: {file.Path}\nPackage path: {file.GamePath ?? "outside the selected root"}\n{file.Asset.Length:N0} bytes · {row.State}" +
                (copies.Any() ? "\nOther copies: " + string.Join("; ", copies) : "");
        }
    }

    public void LoadInventory(IReadOnlyList<ContentFile> files, IEnumerable<string> sources, string? source = null, string? path = null)
    {
        inventory = files;
        ContentSources.Clear();
        foreach (var item in sources) ContentSources.Add(item);
        SelectedSource = source is not null && ContentSources.Contains(source) ? source : ContentSources.FirstOrDefault();
        RefreshFileRows();
        SelectedFile = SourceFiles.FirstOrDefault(row => row.File.Path == path);
        Notify(nameof(FileSummary));
    }

    private void RefreshFileRows()
    {
        SourceFiles.Clear();
        foreach (var file in inventory.Where(file => file.Source == SelectedSource && file.Path.Contains(FileFilter, StringComparison.OrdinalIgnoreCase)))
            SourceFiles.Add(new SourceFileRow(file));
        SelectedFile = null;
    }

    private static List<string> Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public JamProject Snapshot(bool requireAppliedMapDb = true)
    {
        if (requireAppliedMapDb && MapDbDirty)
            throw new InvalidOperationException("Apply the edited mapdb JSON first, or choose Refresh to discard those edits.");
        var sources = Lines(SourcesText);
        var value = new JamProject
        {
            SchemaVersion = Project.SchemaVersion, Id = Project.Id, Title = Project.Title, Profile = Project.Profile, StartMap = Project.StartMap,
            Sources = sources, ReferenceSources = Lines(ReferenceText), Maps = Maps.ToList(), MapDbTemplate = Project.MapDbTemplate,
            SourceSettings = Project.SourceSettings.Where(pair => sources.Contains(pair.Key, StringComparer.Ordinal)).ToDictionary()
        };
        return JsonSerializer.Deserialize<JamProject>(JsonSerializer.Serialize(value, JamProject.JsonOptions), JamProject.JsonOptions)!;
    }

    private string Fingerprint() => JsonSerializer.Serialize(Snapshot(false), JamProject.JsonOptions);

    public void LoadProject(JamProject value, string path, bool saved = true)
    {
        var mapdb = MapDatabase.Export(value);
        Project = value;
        ProjectPath = path;
        SourcesText = string.Join('\n', value.Sources);
        ReferenceText = string.Join('\n', value.ReferenceSources);
        Maps.Clear();
        foreach (var map in value.Maps) Maps.Add(map);
        SelectedMap = Maps.FirstOrDefault();
        MapDbText = mapdb;
        mapDbBaseline = mapdb;
        ReportText = "Project loaded. Run checks after reviewing the content sources.";
        EntityMap = "";
        EntityText = "Select a map and choose Inspect entities to read its compiled entity lump.";
        LoadInventory([], value.Sources);
        if (saved) savedJson = Fingerprint();
    }

    public void SaveProject(string path)
    {
        var value = Snapshot();
        path = Path.GetFullPath(path);
        if (ProjectPath.Length > 0 && !string.Equals(path, ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(path)!;
            var oldDirectory = Path.GetDirectoryName(ProjectPath)!;
            value.SourceSettings = value.SourceSettings.ToDictionary(pair => Path.GetRelativePath(directory, Path.GetFullPath(pair.Key, oldDirectory)).Replace('\\', '/'), pair => pair.Value, StringComparer.Ordinal);
            value.Sources = value.ResolveSources(ProjectPath).Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).ToList();
            value.ReferenceSources = value.ResolveSources(ProjectPath, true).Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).ToList();
        }
        // Save only replaces the project currently open; Save As is a new file.
        value.Save(path, string.Equals(Path.GetFullPath(path), ProjectPath, StringComparison.OrdinalIgnoreCase));
        Project.SchemaVersion = value.SchemaVersion;
        Project.SourceSettings = value.SourceSettings;
        SourcesText = string.Join('\n', value.Sources);
        ReferenceText = string.Join('\n', value.ReferenceSources);
        ProjectPath = Path.GetFullPath(path);
        LoadInventory([], value.Sources);
        savedJson = Fingerprint();
    }

    public void ApplyMapDb()
    {
        var value = Snapshot(false);
        MapDatabase.Import(value, MapDbText);
        LoadProject(value, ProjectPath, false);
    }

    public void RefreshMapDb()
    {
        MapDbText = MapDatabase.Export(Snapshot(false));
        mapDbBaseline = MapDbText;
    }

    public string[] SkinPaths() => string.IsNullOrWhiteSpace(SkinsText) ? [] :
        SkinsText.TrimEnd('\r', '\n').Split('\n').Select(s => s.Trim()).ToArray();

    public void LoadModel(string path)
    {
        var model = Md2Model.Load(path);
        Model = model;
        ModelPath = Path.GetFullPath(path);
        SkinsText = string.Join('\n', model.Skins);
        savedSkins = SkinsText;
        ModelSummary = $"{Path.GetFileName(path)}\n{model.VertexCount:N0} vertices · {model.TriangleCount:N0} triangles · {model.FrameCount:N0} frames\n" +
            $"Skin size {model.Width} × {model.Height} · {model.Skins.Count} / {Md2Model.MaxSkins} skin slots\n" +
            (model.ClassicCompatible ? "Classic-compatible counts\n" : "Extended frame count: requires a rerelease-compatible engine\n") + ModelPath;
    }

    public void SaveModel(string path)
    {
        if (Model is null) throw new InvalidOperationException("Open an MD2 model first.");
        Model.SaveCopy(ModelPath, path, SkinPaths());
        savedSkins = SkinsText;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(name);
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record SourceFileRow(ContentFile File)
{
    public string Name => File.GamePath ?? File.Path;
    public string Size => File.Asset.Length < 1024 ? $"{File.Asset.Length} B" : $"{File.Asset.Length / 1024.0:N1} KiB";
    public string State => File.GamePath is null ? "Outside root" : !File.Included ? "Excluded" :
        JamValidator.ReservedNames.Contains(File.GamePath, StringComparer.OrdinalIgnoreCase) ? "Generated path" : "Included";
}
