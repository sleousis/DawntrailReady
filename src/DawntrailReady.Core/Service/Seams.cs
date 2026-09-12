using DawntrailReady.Core.Packaging;

namespace DawntrailReady.Core.Service;

/// <summary>Time, injectable so the queue can be tested without waiting.</summary>
public interface IServiceClock
{
    DateTimeOffset Now { get; }

    /// <summary>Waits, returning early (without throwing) when the token is cancelled.</summary>
    void Sleep(TimeSpan duration, CancellationToken ct);
}

public sealed class SystemClock : IServiceClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public void Sleep(TimeSpan duration, CancellationToken ct)
    {
        if (duration > TimeSpan.Zero) ct.WaitHandle.WaitOne(duration);
    }
}

/// <summary>Where log lines go. The plugin sends them to /xllog; nothing reaches chat.</summary>
public interface IServiceLog
{
    void Info(string message);
    void Warning(string message, Exception? ex = null);
    void Error(string message, Exception? ex = null);
}

/// <summary>What a quick look at a mod found. Counts are a summary for the list, not a promise.</summary>
public sealed record ModCheck(bool NeedsUpdate, int Materials, int Models);

/// <summary>The outcome of upgrading one installed mod in place.</summary>
public sealed record FolderConversion(bool Changed, int FilesWritten, int FilesAdded, IReadOnlyList<string> Notes, ModpackData? Data);

/// <summary>The outcome of converting a .pmp/.ttmp2 file. OutputPath is null when nothing needed changing.</summary>
public sealed record FileConversion(bool Changed, string? OutputPath, IReadOnlyList<string> Notes);

/// <summary>
/// The file work, behind one seam so the queue's rules can be tested with a fake. The real implementation is
/// <see cref="DawntrailBackend"/>. Every method runs on the worker thread.
/// </summary>
public interface IConversionBackend
{
    ModCheck Check(string modFolder);

    /// <summary>Upgrades the mod folder in place. Honours <paramref name="ct"/> only before anything is written.</summary>
    FolderConversion ConvertFolder(string modFolder, CancellationToken ct);

    /// <summary>True when every game path of <paramref name="expected"/> is still listed in the mod's JSON.</summary>
    bool Verify(string modFolder, ModpackData expected);

    /// <summary>Writes <paramref name="expected"/> again (Penumbra saved its own copy of the JSON over ours).</summary>
    void Reapply(string modFolder, ModpackData expected);

    FileConversion ConvertFile(string path, string tempRoot, CancellationToken ct);
}
