using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using ShartyTools.Core.Formats;
using ShartyTools.Core.IO;
using ShartyTools.Core.Jams;

namespace ShartyTools.Desktop;

public sealed partial class MainWindow : Window
{
    public WorkspaceViewModel Workspace { get; } = new();
    private bool closingConfirmed;
    private bool closingPrompt;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Workspace;
        Closing += OnClosing;
    }

    private async Task Run(Func<Task> action)
    {
        if (Workspace.IsBusy) return;
        Workspace.IsBusy = true;
        try { await action(); }
        catch (Exception error)
        {
            Workspace.Status = "Could not complete the operation: " + error.Message;
        }
        finally { Workspace.IsBusy = false; }
    }

    private async Task<string?> OpenFile(string title, string extension)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> SaveFile(string title, string extension, string name)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = name, DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }]
        });
        return file?.TryGetLocalPath();
    }

    private async Task<bool> ConfirmDiscard(string message)
    {
        var dialog = new Window { Title = "Unsaved changes", Width = 480, Height = 210, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new Button { Content = "Keep editing" };
        var discard = new Button { Content = "Discard changes" };
        cancel.Click += (_, _) => dialog.Close(false);
        discard.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 24,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { cancel, discard } }
            }
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closingConfirmed) return;
        if (Workspace.IsBusy) { e.Cancel = true; Workspace.Status = "Wait for the current operation to finish before closing."; return; }
        if (!Workspace.JamDirty && !Workspace.SkinsDirty) return;
        e.Cancel = true;
        if (closingPrompt) return;
        closingPrompt = true;
        try
        {
            if (await ConfirmDiscard("You have unsaved project or skin-table changes. Save them before closing, or discard them now."))
            {
                closingConfirmed = true;
                Close();
            }
        }
        finally { closingPrompt = false; }
    }

    private async void NewJam(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Workspace.JamDirty && !await ConfirmDiscard("Discard changes to the current jam and create a new project?")) return;
        var path = await SaveFile("Create a jam project", "json", "myjam.sharty.json");
        if (path is null) return;
        var project = new JamProject();
        var content = Path.Combine(Path.GetDirectoryName(path)!, "content");
        GamePath.RejectLinks(content);
        project.Save(path);
        Directory.CreateDirectory(content);
        Workspace.LoadProject(project, path);
        Workspace.Status = "Jam created. Set its title and ID, add submission sources, then discover maps.";
    });

    private async void OpenJam(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Workspace.JamDirty && !await ConfirmDiscard("Discard changes to the current jam and open another project?")) return;
        var path = await OpenFile("Open jam project", "json");
        if (path is null) return;
        var project = await Task.Run(() => JamProject.Load(path));
        Workspace.LoadProject(project, path);
        Workspace.Status = project.SchemaVersion == 1 ? "Project loaded. Save upgrades to schema 2 and retains the original in a .schema1.bak file." : "Project loaded.";
    });

    private async Task SaveProject(bool saveAs)
    {
        var path = saveAs || Workspace.ProjectPath.Length == 0
            ? await SaveFile("Save jam project", "json", Workspace.Project.Id + ".sharty.json") : Workspace.ProjectPath;
        if (path is null) return;
        Workspace.SaveProject(path);
        Workspace.Status = "Saved " + path;
    }
    private async void SaveJam(object? sender, RoutedEventArgs e) => await Run(() => SaveProject(false));
    private async void SaveJamAs(object? sender, RoutedEventArgs e) => await Run(() => SaveProject(true));

    private void RequireProject()
    {
        if (Workspace.ProjectPath.Length == 0) throw new InvalidOperationException("Create or save a jam project first so relative source paths have a home.");
    }

    private async Task AddSource(bool reference, string? extension)
    {
        RequireProject();
        string? path;
        if (extension is not null) path = await OpenFile($"Add {extension.ToUpperInvariant()} source", extension);
        else
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a game content root", AllowMultiple = false });
            path = folders.FirstOrDefault()?.TryGetLocalPath();
        }
        if (path is null) return;
        var relative = Path.GetRelativePath(Path.GetDirectoryName(Workspace.ProjectPath)!, path).Replace('\\', '/');
        if (reference) Workspace.ReferenceText = (Workspace.ReferenceText + "\n" + relative).Trim();
        else
        {
            var snapshot = Workspace.Snapshot();
            await Task.Run(() => JamContent.AddSource(snapshot, Workspace.ProjectPath, path));
            Workspace.LoadProject(snapshot, Workspace.ProjectPath, false);
            await RefreshContentFiles(relative);
        }
        Workspace.Status = "Source added. Review Files; set a content root if maps/ is inside a mod folder.";
    }
    private async void AddContentFolder(object? sender, RoutedEventArgs e) => await Run(() => AddSource(false, null));
    private async void AddContentPak(object? sender, RoutedEventArgs e) => await Run(() => AddSource(false, "pak"));
    private async void AddContentZip(object? sender, RoutedEventArgs e) => await Run(() => AddSource(false, "zip"));
    private async void AddReferenceFolder(object? sender, RoutedEventArgs e) => await Run(() => AddSource(true, null));
    private async void AddReferencePak(object? sender, RoutedEventArgs e) => await Run(() => AddSource(true, "pak"));

    private async void DiscoverMaps(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireProject();
        var snapshot = Workspace.Snapshot();
        Workspace.Status = "Reading submission maps…";
        var count = await Task.Run(() => JamValidator.DiscoverMaps(snapshot, Workspace.ProjectPath));
        Workspace.LoadProject(snapshot, Workspace.ProjectPath, false);
        Workspace.Status = $"Discovered {count} new maps. Review titles, credits, game modes and start map, then save.";
    });

    private async void RunChecks(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireProject();
        var snapshot = Workspace.Snapshot();
        Workspace.Status = "Checking maps and asset references…";
        var report = await Task.Run(() => JamValidator.Validate(snapshot, Workspace.ProjectPath));
        Workspace.ReportText = report.ToString();
        this.FindControl<TabControl>("JamTabs")!.SelectedIndex = 2;
        Workspace.Status = report.HasErrors ? "Checks found errors. Review the report before building." : "Checks complete. Review warnings and playtest before release.";
    });

    private async Task Build(string extension)
    {
        RequireProject();
        var path = await SaveFile("Build jam package", extension, Workspace.Project.Id + "." + extension);
        if (path is null) return;
        var snapshot = Workspace.Snapshot();
        Workspace.Status = "Validating and assembling package…";
        var result = await Task.Run(() => JamBuilder.Build(snapshot, Workspace.ProjectPath, path));
        Workspace.ReportText = result.Report + $"\n\nBuilt {result.Path}\nSHA-256 {result.Sha256}";
        this.FindControl<TabControl>("JamTabs")!.SelectedIndex = 2;
        Workspace.Status = $"Built {Path.GetFileName(path)} ({result.Bytes / 1024.0 / 1024.0:F1} MiB). Full checksum is in the checks report.";
    }
    private async void BuildZip(object? sender, RoutedEventArgs e) => await Run(() => Build("zip"));
    private async void BuildPak(object? sender, RoutedEventArgs e) => await Run(() => Build("pak"));

    private void MoveMap(int delta)
    {
        if (Workspace.SelectedMap is null) return;
        var current = Workspace.Maps.IndexOf(Workspace.SelectedMap);
        var next = current + delta;
        if (next >= 0 && next < Workspace.Maps.Count) Workspace.Maps.Move(current, next);
    }
    private void MapUp(object? sender, RoutedEventArgs e) => MoveMap(-1);
    private void MapDown(object? sender, RoutedEventArgs e) => MoveMap(1);
    private void RemoveMap(object? sender, RoutedEventArgs e)
    {
        if (Workspace.SelectedMap is { } map) Workspace.Maps.Remove(map);
    }

    private async void InspectEntities(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireProject();
        if (Workspace.SelectedMap is not { } map) return;
        var snapshot = Workspace.Snapshot();
        var text = await Task.Run(() =>
        {
            var catalog = JamContent.CreateCatalog(snapshot, Workspace.ProjectPath);
            if (!catalog.Files.TryGetValue($"maps/{GamePath.MapFileFromLaunch(map.Bsp)}.bsp", out var file)) throw new FileNotFoundException("Selected map is missing from the content sources.");
            return BspFile.Read(file.ReadAll()).EntitySource;
        });
        Workspace.EntityText = text;
        Workspace.EntityMap = GamePath.MapFileFromLaunch(map.Bsp);
        this.FindControl<TabControl>("JamTabs")!.SelectedIndex = 3;
        Workspace.Status = $"Read entity lump from {map.Bsp}.bsp.";
    });

    private async Task RefreshContentFiles(string? source = null, string? path = null)
    {
        RequireProject();
        var snapshot = Workspace.Snapshot();
        var files = await Task.Run(() => JamContent.Inspect(snapshot, Workspace.ProjectPath));
        Workspace.LoadInventory(files, snapshot.Sources, source ?? Workspace.SelectedSource, path);
        this.FindControl<TabControl>("JamTabs")!.SelectedItem = this.FindControl<TabItem>("FilesTab");
    }

    private async void RefreshFiles(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        await RefreshContentFiles();
        Workspace.Status = "File inventory refreshed. Changes to inclusion and roots are saved with the project.";
    });

    private async Task SelectFile(bool included)
    {
        if (Workspace.SelectedFile is not { File: var file } || file.GamePath is null) return;
        var snapshot = Workspace.Snapshot();
        JamContent.SetIncluded(snapshot, file.Source, file.Path, included);
        Workspace.LoadProject(snapshot, Workspace.ProjectPath, false);
        await RefreshContentFiles(file.Source, file.Path);
        Workspace.Status = $"{(included ? "Included" : "Excluded")} {file.Path}. Run checks after curating files, then save the project.";
    }
    private async void IncludeFile(object? sender, RoutedEventArgs e) => await Run(() => SelectFile(true));
    private async void ExcludeFile(object? sender, RoutedEventArgs e) => await Run(() => SelectFile(false));

    private async void SetSourceRoot(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireProject();
        if (Workspace.SelectedSource is not { } source) throw new InvalidOperationException("Refresh files and select a source first.");
        var snapshot = Workspace.Snapshot();
        var dialog = new Window { Title = "Content root", Width = 520, Height = 290, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new TextBox { Text = snapshot.SourceSettings.GetValueOrDefault(source)?.Root ?? "", PlaceholderText = "Source root (empty)" };
        var apply = new Button { Content = "Apply root" };
        var cancel = new Button { Content = "Cancel" };
        apply.Click += (_, _) => dialog.Close(root.Text ?? "");
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock { Text = "For pack/maps/example.bsp, enter pack. Leave empty when maps/ is at the source root. Paths are case-sensitive; files outside the root are omitted.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                root, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { apply, cancel } }
            }
        };
        var value = await dialog.ShowDialog<string?>(this);
        if (value is null) return;
        JamContent.SetRoot(snapshot, source, value);
        var files = await Task.Run(() => JamContent.Inspect(snapshot, Workspace.ProjectPath));
        Workspace.LoadProject(snapshot, Workspace.ProjectPath, false);
        Workspace.LoadInventory(files, snapshot.Sources, source);
        Workspace.Status = "Content root updated. Review omitted files, including submission readmes and licenses, before building.";
    });

    private async void ImportSourceMapDb(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Workspace.SelectedFile is not { File: var file }) return;
        if (Workspace.Maps.Count > 0 && !await ConfirmDiscard("Import replaces the current map listing and excludes this source mapdb from packaging. Continue?")) return;
        var snapshot = Workspace.Snapshot();
        await Task.Run(() => JamContent.ImportMapDb(snapshot, Workspace.ProjectPath, file.Source, file.Path));
        Workspace.LoadProject(snapshot, Workspace.ProjectPath, false);
        await RefreshContentFiles(file.Source, file.Path);
        Workspace.Status = "Imported mapdb and excluded its source copy. Other generated files still need to be excluded explicitly; review the map list and save.";
    });

    private async void ImportMapDb(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = await OpenFile("Import mapdb", "json");
        if (path is null) return;
        if (Workspace.Maps.Count > 0 && !await ConfirmDiscard("Import replaces the current map listing. Content sources stay the same. Continue?")) return;
        Workspace.MapDbText = await File.ReadAllTextAsync(path);
        Workspace.ApplyMapDb();
        Workspace.Status = "Imported mapdb. Unknown fields and episodes are preserved; review the map list and save.";
    });
    private async void ApplyMapDb(object? sender, RoutedEventArgs e) => await Run(() =>
    {
        Workspace.ApplyMapDb();
        Workspace.Status = "Applied mapdb JSON to the project. Save to keep these changes.";
        return Task.CompletedTask;
    });
    private async void RefreshMapDb(object? sender, RoutedEventArgs e) => await Run(() =>
    {
        Workspace.RefreshMapDb();
        Workspace.Status = "Rebuilt mapdb from current map details.";
        return Task.CompletedTask;
    });
    private async void ExportMapDb(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = await SaveFile("Export map database", "json", "mapdb.json");
        if (path is null) return;
        AtomicFile.WriteText(path, MapDatabase.Export(Workspace.Snapshot()));
        Workspace.Status = "Exported " + path;
    });
    private async void SaveReport(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        var path = await SaveFile("Save check report", "txt", "jam-checks.txt");
        if (path is not null) { AtomicFile.WriteText(path, Workspace.ReportText); Workspace.Status = "Saved " + path; }
    });
    private async void ExportEntities(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Workspace.EntityMap.Length == 0) throw new InvalidOperationException("Inspect a map's entities before exporting them.");
        var path = await SaveFile("Export entity text", "ent", Path.GetFileName(Workspace.EntityMap) + ".ent");
        if (path is not null) { AtomicFile.WriteText(path, Workspace.EntityText + "\n"); Workspace.Status = "Saved " + path; }
    });

    private async void OpenModel(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Workspace.SkinsDirty && !await ConfirmDiscard("Discard the unsaved skin-table edits and open another model?")) return;
        var path = await OpenFile("Open MD2 model", "md2");
        if (path is null) return;
        Workspace.LoadModel(path);
        Workspace.Status = "Model loaded. Edit the ordered skin paths, then save a new copy.";
    });
    private async void ValidateSkins(object? sender, RoutedEventArgs e) => await Run(() =>
    {
        if (Workspace.Model is null) throw new InvalidOperationException("Open an MD2 model first.");
        _ = Workspace.Model.WithSkins(Workspace.SkinPaths());
        Workspace.Status = "Skin table is valid. Make sure the image files exist at these paths and match the model's dimensions.";
        return Task.CompletedTask;
    });
    private async void SaveModel(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (Workspace.Model is null) throw new InvalidOperationException("Open an MD2 model first.");
        var path = await SaveFile("Save edited MD2 copy", "md2", Path.GetFileNameWithoutExtension(Workspace.ModelPath) + "-edited.md2");
        if (path is null) return;
        Workspace.SaveModel(path);
        Workspace.Status = "Saved edited copy to " + path;
    });
}
