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

    private static List<string> Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public JamProject Snapshot(bool requireAppliedMapDb = true)
    {
        if (requireAppliedMapDb && MapDbDirty)
            throw new InvalidOperationException("Apply the edited mapdb JSON first, or choose Refresh to discard those edits.");
        Project.Sources = Lines(SourcesText);
        Project.ReferenceSources = Lines(ReferenceText);
        Project.Maps = Maps.ToList();
        return JsonSerializer.Deserialize<JamProject>(JsonSerializer.Serialize(Project, JamProject.JsonOptions), JamProject.JsonOptions)!;
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
        if (saved) savedJson = Fingerprint();
    }

    public void SaveProject(string path)
    {
        var value = Snapshot();
        path = Path.GetFullPath(path);
        if (ProjectPath.Length > 0 && !string.Equals(path, ProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(path)!;
            value.Sources = value.ResolveSources(ProjectPath).Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).ToList();
            value.ReferenceSources = value.ResolveSources(ProjectPath, true).Select(p => Path.GetRelativePath(directory, p).Replace('\\', '/')).ToList();
        }
        // Save only replaces the project currently open; Save As is a new file.
        value.Save(path, string.Equals(Path.GetFullPath(path), ProjectPath, StringComparison.OrdinalIgnoreCase));
        SourcesText = string.Join('\n', value.Sources);
        ReferenceText = string.Join('\n', value.ReferenceSources);
        ProjectPath = Path.GetFullPath(path);
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
