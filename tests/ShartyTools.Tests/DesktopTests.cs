using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ShartyTools.Core.Jams;
using ShartyTools.Desktop;

[assembly: AvaloniaTestApplication(typeof(ShartyTools.Tests.DesktopTestApp))]

namespace ShartyTools.Tests;

public static class DesktopTestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia().UseHarfBuzz().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class DesktopTests
{
    [AvaloniaFact]
    public void DesktopRendersBothToolsAndBindsLoadedProject()
    {
        var window = new MainWindow();
        var project = new JamProject
        {
            Id = "foundryjam", Title = "Foundry Jam", StartMap = "fj_intro",
            Sources = ["submissions/final", "shared-assets"], ReferenceSources = ["../../Quake2/rerelease/baseq2"],
            Maps = [
                new() { Bsp = "fj_intro", Title = "Arrival at the Foundry", Author = "Example Mapper", Episode = "foundryjam", Sp = true, Coop = true },
                new() { Bsp = "fj_hub", Title = "The Assembly Line", Author = "Example Mapper", Sp = true },
                new() { Bsp = "fj_01", Title = "Molten Circuit", Author = "Example Mapper", Sp = true }
            ]
        };
        window.Workspace.LoadProject(project, "");
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Foundry Jam", window.FindControl<TextBox>("JamTitle")!.Text);
        Assert.Equal(3, window.FindControl<ListBox>("MapsList")!.ItemCount);
        Assert.True(window.FindControl<TextBox>("JamTitle")!.Bounds.Width > 100);
        Capture(window, "jam-manager.png");
        window.FindControl<TabControl>("ToolTabs")!.SelectedIndex = 1;
        window.Workspace.SkinsText = "models/monsters/soldier/skin.pcx\nmodels/monsters/soldier/foundry.pcx";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(window.Workspace.SkinsText, window.FindControl<TextBox>("SkinEditor")!.Text);
        Capture(window, "md2-editor.png");
        window.Workspace.SkinsText = ""; // Restore clean state so disposal does not open a prompt.
        window.Close();
    }

    private static void Capture(Window window, string name)
    {
        // Opt-in render-target captures only: no desktop capture or OS input injection.
        if (Environment.GetEnvironmentVariable("SHARTY_CAPTURE_UI") != "1") return;
        var directory = Path.Combine(TestFolder.RepositoryRoot, ".artifacts", "verification");
        Directory.CreateDirectory(directory);
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        image.Save(Path.Combine(directory, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }
}
