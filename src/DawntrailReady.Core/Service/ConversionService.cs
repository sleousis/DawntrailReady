using System.Collections.Concurrent;
using DawntrailReady.Core.Packaging;

namespace DawntrailReady.Core.Service;

public enum HostRequestKind { Reload, Install }

/// <summary>Something only Penumbra can do, run by the plugin on the framework thread.</summary>
public sealed record HostRequest(HostRequestKind Kind, string ModRoot, string ModDirectory, string ModName, string Path);

public sealed record ScanHit(string ModDirectory, string ModName, int Materials, int Models);

public sealed record ScanState(bool Running, int Checked, int Total, IReadOnlyList<ScanHit> Hits, DateTimeOffset? FinishedAt, int Failed);

public sealed record ServiceSnapshot(bool Busy, string Status, int Done, int Total, ScanState? Scan, IReadOnlyList<ActivityEntry> Recent);

public sealed class ServiceOptions
{
    /// <summary>Penumbra may still be saving the new mod right after ModAdded; wait this long before touching it.</summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Wait after a reload before checking that Penumbra kept our JSON changes.</summary>
    public TimeSpan VerifyDelay { get; init; } = TimeSpan.FromSeconds(1.5);

    public string TempRoot { get; init; } = Path.GetTempPath();

    public int RecentCount { get; init; } = 30;
}

/// <summary>
/// One queue, one background thread, one job at a time. Import events, scans, "update these" and file
/// conversions all go through here, so two jobs never touch the same mod at once. Anything that needs Penumbra
/// (reload, install) is handed back as a <see cref="HostRequest"/>.
/// </summary>
public sealed partial class ConversionService : IDisposable
{
    private abstract record Job(DateTimeOffset NotBefore);
    private sealed record ImportJob(string Root, string Dir, string Name, DateTimeOffset NotBefore) : Job(NotBefore);
    private sealed record ScanJob(string Root, IReadOnlyDictionary<string, string> Mods) : Job(DateTimeOffset.MinValue);
    private sealed record FixJob(string Root, IReadOnlyList<ScanHit> Hits) : Job(DateTimeOffset.MinValue);
    private sealed record FileJob(string Path) : Job(DateTimeOffset.MinValue);
    private sealed record VerifyJob(string Root, string Dir, string Name, DateTimeOffset NotBefore) : Job(NotBefore);
    private sealed record PendingVerify(ModpackData Data, bool Reapplied, DateTimeOffset At);

    private readonly IConversionBackend backend;
    private readonly IServiceClock clock;
    private readonly IActivityLog activity;
    private readonly IServiceLog log;
    private readonly ServiceOptions options;
    private readonly BlockingCollection<Job> queue = new(new ConcurrentQueue<Job>());
    private readonly CancellationTokenSource cts = new();
    private readonly Thread? worker;
    private readonly Lock gate = new();

    // Guarded by gate.
    private readonly HashSet<string> queuedDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingVerify> awaitingVerify = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HostRequest> hostRequests = [];
    private readonly List<ActivityEntry> recent;
    private bool running;
    private bool scanQueued;
    private string status = "";
    private int done, total;
    private ScanState? scan;
    private bool disposed;

    /// <summary>Raised on the worker thread when <see cref="DrainHostRequests"/> has something.</summary>
    public event Action? HostRequestsPending;

    public ConversionService(IConversionBackend backend, IServiceClock clock, IActivityLog activity, IServiceLog log,
        ServiceOptions? options = null, bool startWorker = true)
    {
        this.backend = backend;
        this.clock = clock;
        this.activity = activity;
        this.log = log;
        this.options = options ?? new ServiceOptions();
        recent = [.. activity.ReadRecent(this.options.RecentCount)];
        if (!startWorker) return;
        worker = new Thread(Loop) { Name = "DawntrailReady worker", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        worker.Start();
    }

    public ServiceSnapshot Snapshot
    {
        get
        {
            lock (gate)
                return new ServiceSnapshot(running || queue.Count > 0, status, done, total, scan, [.. recent]);
        }
    }

    /// <summary>A mod Penumbra just imported. Returns false when it is already waiting.</summary>
    public bool EnqueueImport(string modRoot, string modDirectory, string modName)
    {
        lock (gate)
        {
            if (disposed || !queuedDirs.Add(modDirectory)) return false;
        }
        queue.Add(new ImportJob(modRoot, modDirectory, modName, clock.Now + options.SettleDelay));
        return true;
    }

    /// <summary>Checks every installed mod. Only one scan at a time.</summary>
    public bool EnqueueScan(string modRoot, IReadOnlyDictionary<string, string> mods)
    {
        lock (gate)
        {
            if (disposed || scanQueued || scan is { Running: true }) return false;
            scanQueued = true;
            scan = new ScanState(true, 0, mods.Count, [], null, 0);
        }
        queue.Add(new ScanJob(modRoot, mods));
        return true;
    }

    /// <summary>Updates exactly these mods, in this order.</summary>
    public bool EnqueueFixAll(string modRoot, IReadOnlyList<ScanHit> hits)
    {
        List<ScanHit> accepted;
        lock (gate)
        {
            if (disposed) return false;
            accepted = hits.Where(h => queuedDirs.Add(h.ModDirectory)).ToList();
        }
        if (accepted.Count == 0) return false;
        queue.Add(new FixJob(modRoot, accepted));
        return true;
    }

    public bool EnqueueConvertFile(string path)
    {
        lock (gate)
            if (disposed) return false;
        queue.Add(new FileJob(path));
        return true;
    }

    /// <summary>The plugin reloaded this mod in Penumbra; check a moment later that our changes are still there.</summary>
    public void ReportReloaded(string modRoot, string modDirectory, string modName)
    {
        lock (gate)
        {
            if (disposed || !awaitingVerify.ContainsKey(modDirectory)) return;
        }
        queue.Add(new VerifyJob(modRoot, modDirectory, modName, clock.Now + options.VerifyDelay));
    }

    public IReadOnlyList<HostRequest> DrainHostRequests()
    {
        lock (gate)
        {
            var copy = hostRequests.ToList();
            hostRequests.Clear();
            return copy;
        }
    }

    /// <summary>Runs one queued job on the calling thread. For tests and for a service built without a worker.</summary>
    public bool RunOnce()
    {
        if (!queue.TryTake(out var job)) return false;
        Run(job, cts.Token);
        return true;
    }

    private void Loop()
    {
        try
        {
            foreach (var job in queue.GetConsumingEnumerable(cts.Token))
            {
                // This is a thread of our own: an exception escaping it would end the whole game process. Every job
                // already records its own failures; this catches anything that slips past them.
                try
                {
                    Run(job, cts.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    try { log.Error("A job failed unexpectedly", ex); }
                    catch (Exception) { /* nothing left to report to */ }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // disposed
        }
        catch (ObjectDisposedException)
        {
            // disposed while waiting for the next job
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        cts.Cancel();
        // A job in its write phase finishes; everything not started is dropped.
        if (worker is null || worker.Join(TimeSpan.FromSeconds(10)))
        {
            cts.Dispose();
            queue.Dispose();
        }
    }
}
