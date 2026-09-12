using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DawntrailReady.Core.Service;
using DawntrailReady.Integrations;
using DawntrailReady.Windows;

namespace DawntrailReady;

/// <summary>
/// The entry point. Idle, the plugin costs nothing per frame: it waits for Penumbra's ModAdded event, and draws
/// only while its window or file picker is open. All file work runs on one background thread.
/// </summary>
public sealed class Plugin : IDalamudPlugin, IMainWindowActions
{
    private const string Command = "/dtready";

    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly Configuration config;
    private readonly PenumbraIpc penumbra;
    private readonly ConversionService service;
    private readonly WindowSystem windows = new("DawntrailReady");
    private readonly MainWindow main;
    private readonly FileDialogManager fileDialog = new();
    private bool drawing;
    private bool dialogOpen;
    private bool disposed;

    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IFramework framework, IChatGui chat, IPluginLog log, IDataManager data)
    {
        this.pi = pi;
        this.commands = commands;
        this.framework = framework;
        this.chat = chat;
        this.log = log;

        config = LoadConfig(pi, log, chat);
        if (config.Migrate()) Save();

        var folder = pi.GetPluginConfigDirectory();
        var work = Path.Combine(folder, "work");
        try { if (Directory.Exists(work)) Directory.Delete(work, true); }
        catch (Exception ex) { log.Debug(ex, "Could not clear the old work folder"); }

        var activity = new JsonLinesActivityLog(new DiskAppendOnlyFile(Path.Combine(folder, "activity.jsonl")));
        service = new ConversionService(new DawntrailBackend(new DalamudGameData(data, log)), new SystemClock(), activity,
            new PluginServiceLog(log), new ServiceOptions { TempRoot = work });
        service.HostRequestsPending += OnHostRequestsPending;

        penumbra = new PenumbraIpc(pi, log);
        penumbra.ModAdded += OnModAdded;

        main = new MainWindow(config, Save, this);
        windows.AddWindow(main);

        commands.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open Dawntrail Ready. \"/dtready scan\" checks your mods; \"/dtready convert <file>\" updates a .pmp or .ttmp2." });
        pi.UiBuilder.OpenMainUi += OpenMain;
        pi.UiBuilder.OpenConfigUi += OpenMain;
    }

    public bool PenumbraReady => penumbra.IsAvailable;

    public ServiceSnapshot Snapshot => service.Snapshot;

    /// <summary>Penumbra has finished importing a mod. Runs on Penumbra's thread: only queue here.</summary>
    private void OnModAdded(string modDirectory)
    {
        if (!config.AutoConvertOnImport) return;
        var root = penumbra.GetModDirectory();
        if (root is null) return;
        var name = penumbra.GetModList().GetValueOrDefault(modDirectory, modDirectory);
        service.EnqueueImport(root, modDirectory, name);
    }

    public void StartScan()
    {
        var root = penumbra.GetModDirectory();
        if (root is null)
        {
            log.Warning("Scan: Penumbra's mod folder isn't available");
            return;
        }
        service.EnqueueScan(root, penumbra.GetModList());
    }

    public void UpdateMods(IReadOnlyList<ScanHit> hits)
    {
        var root = penumbra.GetModDirectory();
        if (root is not null) service.EnqueueFixAll(root, hits);
    }

    public void PickModFile()
    {
        dialogOpen = true;
        StartDrawing();
        fileDialog.OpenFileDialog("Convert a mod file", "Mod files{.pmp,.ttmp2,.ttmp}", (ok, path) =>
        {
            dialogOpen = false;
            if (ok && !string.IsNullOrEmpty(path)) service.EnqueueConvertFile(path);
        });
    }

    /// <summary>The worker has something for Penumbra; do it on the framework thread, where Penumbra expects it.</summary>
    private void OnHostRequestsPending()
    {
        if (disposed) return;
        _ = framework.RunOnFrameworkThread(RunHostRequests);
    }

    private void RunHostRequests()
    {
        if (disposed) return;
        Dictionary<string, string>? names = null;
        foreach (var request in service.DrainHostRequests())
        {
            switch (request.Kind)
            {
                case HostRequestKind.Reload:
                    names ??= penumbra.GetModList();
                    var name = names.GetValueOrDefault(request.ModDirectory, request.ModName);
                    if (penumbra.ReloadMod(request.ModDirectory, name))
                        service.ReportReloaded(request.ModRoot, request.ModDirectory, name);
                    break;
                case HostRequestKind.Install:
                    penumbra.InstallMod(request.Path);
                    break;
            }
        }
    }

    private void OnCommand(string command, string args)
    {
        // "/dtready convert <file>": the same as "Convert a mod file..." in the window, for a file named directly.
        var trimmed = args.Trim();
        if (trimmed.StartsWith("convert", StringComparison.OrdinalIgnoreCase))
        {
            var path = trimmed["convert".Length..].Trim().Trim('"');
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (path.Length == 0 || !File.Exists(path) || ext is not (".pmp" or ".ttmp2" or ".ttmp"))
            {
                chat.PrintError($"{Command} convert needs the full path of a .pmp, .ttmp2 or .ttmp file.", "Dawntrail Ready");
                return;
            }
            service.EnqueueConvertFile(path);
            chat.Print($"Converting {Path.GetFileName(path)}...", "Dawntrail Ready");
            return;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "":
                main.Toggle();
                if (main.IsOpen) StartDrawing();
                break;
            case "scan":
                OpenMain();
                StartScan();
                break;
            default:
                // An unknown word should never silently do something else. Say what exists.
                chat.Print($"{Command} opens the window, {Command} scan checks your mods, {Command} convert <file> updates a mod file.", "Dawntrail Ready");
                break;
        }
    }

    private void OpenMain()
    {
        main.IsOpen = true;
        StartDrawing();
    }

    // Draw is attached only while the window or the file picker is open.
    private void StartDrawing()
    {
        if (drawing || disposed) return;
        drawing = true;
        pi.UiBuilder.Draw += DrawUi;
    }

    private void DrawUi()
    {
        windows.Draw();
        fileDialog.Draw();
        if (!main.IsOpen && !dialogOpen)
        {
            drawing = false;
            pi.UiBuilder.Draw -= DrawUi;
        }
    }

    /// <summary>Every save goes through the framework thread, where the window also edits the settings.</summary>
    private void Save()
    {
        if (disposed) return;
        if (framework.IsInFrameworkUpdateThread) config.Save(pi);
        else _ = framework.RunOnFrameworkThread(() => config.Save(pi));
    }

    /// <summary>Unreadable settings are copied aside before a fresh start, so nothing is lost for good.</summary>
    private static Configuration LoadConfig(IDalamudPluginInterface pi, IPluginLog log, IChatGui chat)
    {
        Configuration? loaded = null;
        try { loaded = pi.GetPluginConfig() as Configuration; }
        catch (Exception ex) { log.Error(ex, "Settings could not be read"); }
        if (loaded is not null) return loaded;

        try
        {
            var file = pi.ConfigFile;
            if (file.Exists && file.Length > 0)
            {
                var backup = file.FullName + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                file.CopyTo(backup, overwrite: false);
                chat.PrintError($"Dawntrail Ready could not read its settings and started fresh. The old file was kept as {Path.GetFileName(backup)}.", "Dawntrail Ready");
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not keep a copy of the unreadable settings");
        }
        return new Configuration();
    }

    public void Dispose()
    {
        disposed = true;
        penumbra.ModAdded -= OnModAdded;
        penumbra.Dispose();
        service.HostRequestsPending -= OnHostRequestsPending;
        service.Dispose();
        if (drawing) pi.UiBuilder.Draw -= DrawUi;
        pi.UiBuilder.OpenMainUi -= OpenMain;
        pi.UiBuilder.OpenConfigUi -= OpenMain;
        commands.RemoveHandler(Command);
        windows.RemoveAllWindows();
    }
}
