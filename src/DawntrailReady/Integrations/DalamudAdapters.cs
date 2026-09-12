using Dalamud.Plugin.Services;
using DawntrailReady.Core.Game;
using DawntrailReady.Core.Service;

namespace DawntrailReady.Integrations;

/// <summary>Vanilla game files through Lumina, one read at a time (called from the worker thread).</summary>
public sealed class DalamudGameData(IDataManager data, IPluginLog log) : IGameData
{
    private readonly Lock gate = new();

    public bool FileExists(string gamePath)
    {
        lock (gate)
        {
            try
            {
                return data.FileExists(gamePath);
            }
            catch (Exception ex)
            {
                log.Debug(ex, $"FileExists failed for {gamePath}");
                return false;
            }
        }
    }

    public byte[]? ReadFile(string gamePath)
    {
        lock (gate)
        {
            try
            {
                return data.GetFile(gamePath)?.Data;
            }
            catch (Exception ex)
            {
                log.Debug(ex, $"GetFile failed for {gamePath}");
                return null;
            }
        }
    }
}

/// <summary>The worker's log lines go to /xllog only; nothing reaches chat.</summary>
public sealed class PluginServiceLog(IPluginLog log) : IServiceLog
{
    public void Info(string message) => log.Information(message);

    public void Warning(string message, Exception? ex = null)
    {
        if (ex is null) log.Warning(message);
        else log.Warning(ex, message);
    }

    public void Error(string message, Exception? ex = null)
    {
        if (ex is null) log.Error(message);
        else log.Error(ex, message);
    }
}
