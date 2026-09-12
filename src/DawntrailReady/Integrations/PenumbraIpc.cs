using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace DawntrailReady.Integrations;

/// <summary>
/// Penumbra over raw Dalamud IPC (API 5), no Penumbra.Api package. Every call is wrapped: when Penumbra is
/// missing, unloading or older, calls return nothing and the plugin simply waits.
/// Reload and install mutate Penumbra's state without locks, so call them on the framework thread only.
/// </summary>
public sealed class PenumbraIpc : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<(int, int)> apiVersion;
    private readonly ICallGateSubscriber<string> getModDirectory;
    private readonly ICallGateSubscriber<Dictionary<string, string>> getModList;
    private readonly ICallGateSubscriber<string, string, int> reloadMod;
    private readonly ICallGateSubscriber<string, int> installMod;
    private readonly ICallGateSubscriber<string, object?> modAdded;
    private readonly ICallGateSubscriber<object?> initialized;
    private readonly ICallGateSubscriber<object?> disposed;
    private readonly ICallGateSubscriber<string, bool, object?> modDirectoryChanged;

    /// <summary>Penumbra finished importing a mod; the argument is its folder name under the mod root.</summary>
    public event Action<string>? ModAdded;

    public PenumbraIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;
        apiVersion = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5");
        getModDirectory = pi.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
        getModList = pi.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
        reloadMod = pi.GetIpcSubscriber<string, string, int>("Penumbra.ReloadMod.V5");
        installMod = pi.GetIpcSubscriber<string, int>("Penumbra.InstallMod.V5");
        modAdded = pi.GetIpcSubscriber<string, object?>("Penumbra.ModAdded");
        initialized = pi.GetIpcSubscriber<object?>("Penumbra.Initialized");
        disposed = pi.GetIpcSubscriber<object?>("Penumbra.Disposed");
        modDirectoryChanged = pi.GetIpcSubscriber<string, bool, object?>("Penumbra.ModDirectoryChanged");

        modAdded.Subscribe(OnModAdded);
        initialized.Subscribe(OnInitialized);
        disposed.Subscribe(OnDisposed);
        modDirectoryChanged.Subscribe(OnModDirectoryChanged);
    }

    /// <summary>Installed, loaded and speaking API 5.</summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                if (!pi.InstalledPlugins.Any(p => p.InternalName == "Penumbra" && p.IsLoaded)) return false;
                return apiVersion.InvokeFunc().Item1 == 5;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public string? GetModDirectory()
    {
        try
        {
            var dir = getModDirectory.InvokeFunc();
            return string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) ? null : dir;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Penumbra.GetModDirectory unavailable");
            return null;
        }
    }

    /// <summary>Mod folder name to display name.</summary>
    public Dictionary<string, string> GetModList()
    {
        try
        {
            return getModList.InvokeFunc() ?? [];
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Penumbra.GetModList unavailable");
            return [];
        }
    }

    /// <summary>Penumbra re-reads the mod's JSON. Framework thread only.</summary>
    public bool ReloadMod(string modDirectory, string modName)
    {
        try
        {
            var ec = reloadMod.InvokeFunc(modDirectory, modName);
            if (ec != 0) log.Warning($"Penumbra could not reload {modName} (code {ec})");
            return ec == 0;
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"Penumbra reload failed for {modName}");
            return false;
        }
    }

    /// <summary>Queues a mod file for import, as if the player had picked it. Framework thread only.</summary>
    public bool InstallMod(string path)
    {
        try
        {
            var ec = installMod.InvokeFunc(path);
            if (ec != 0) log.Warning($"Penumbra could not queue {Path.GetFileName(path)} for import (code {ec})");
            return ec == 0;
        }
        catch (Exception ex)
        {
            log.Warning(ex, $"Penumbra install failed for {Path.GetFileName(path)}");
            return false;
        }
    }

    private void OnModAdded(string modDirectory)
    {
        try
        {
            ModAdded?.Invoke(modDirectory);
        }
        catch (Exception ex)
        {
            // Never throw back into Penumbra's import.
            log.Error(ex, $"Handling a new mod ({modDirectory}) failed");
        }
    }

    private void OnInitialized() => log.Debug("Penumbra is ready");

    private void OnDisposed() => log.Debug("Penumbra unloaded");

    private void OnModDirectoryChanged(string path, bool valid) => log.Debug($"Penumbra mod folder is now {path} (valid: {valid})");

    public void Dispose()
    {
        modAdded.Unsubscribe(OnModAdded);
        initialized.Unsubscribe(OnInitialized);
        disposed.Unsubscribe(OnDisposed);
        modDirectoryChanged.Unsubscribe(OnModDirectoryChanged);
    }
}
