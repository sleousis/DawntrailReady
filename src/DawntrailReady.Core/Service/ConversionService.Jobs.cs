using DawntrailReady.Core.Packaging;

namespace DawntrailReady.Core.Service;

public sealed partial class ConversionService
{
    private void Run(Job job, CancellationToken ct)
    {
        lock (gate) running = true;
        try
        {
            if (job.NotBefore > clock.Now) clock.Sleep(job.NotBefore - clock.Now, ct);
            if (ct.IsCancellationRequested) return;

            switch (job)
            {
                case ImportJob j: RunImport(j, ct); break;
                case ScanJob j: RunScan(j, ct); break;
                case FixJob j: RunFix(j, ct); break;
                case FileJob j: RunFile(j, ct); break;
                case VerifyJob j: RunVerify(j); break;
            }
        }
        catch (OperationCanceledException)
        {
            // stopping: whatever wasn't reached isn't done, and isn't parked for later
        }
        finally
        {
            lock (gate)
            {
                running = false;
                status = "";
                done = total = 0;
            }
        }
    }

    private void RunImport(ImportJob job, CancellationToken ct)
    {
        lock (gate)
        {
            queuedDirs.Remove(job.Dir);
            status = $"Checking {job.Name}";
        }
        try
        {
            // ConvertFolder runs the cheap header check first, so a Dawntrail-ready import is nearly free.
            ConvertInstalled(job.Root, job.Dir, job.Name, "Updated on import", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(job.Name, "Could not update on import", ex);
        }
    }

    private void RunScan(ScanJob job, CancellationToken ct)
    {
        lock (gate) scanQueued = false;
        var hits = new List<ScanHit>();
        int checkedCount = 0, failed = 0;
        foreach (var (dir, name) in job.Mods.OrderBy(m => m.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var check = backend.Check(ModPaths.Resolve(job.Root, dir));
                if (check.NeedsUpdate) hits.Add(new ScanHit(dir, name, check.Materials, check.Models));
            }
            catch (Exception ex)
            {
                failed++;
                log.Warning($"Scan: could not read {name}", ex);
            }
            checkedCount++;
            lock (gate)
            {
                status = "Checking your mods";
                scan = new ScanState(true, checkedCount, job.Mods.Count, [.. hits], null, failed);
            }
        }
        lock (gate) scan = new ScanState(false, checkedCount, job.Mods.Count, hits, clock.Now, failed);
        log.Info($"Scan: {checkedCount} mods checked, {hits.Count} need updating, {failed} unreadable");
        Record(new ActivityEntry(clock.Now, "Scan", "", hits.Count == 0
            ? $"All {checkedCount} mods are Dawntrail-ready"
            : $"{hits.Count} of {checkedCount} mods need updating", failed > 0));
    }

    private void RunFix(FixJob job, CancellationToken ct)
    {
        lock (gate)
        {
            done = 0;
            total = job.Hits.Count;
        }
        for (var i = 0; i < job.Hits.Count; i++)
        {
            var hit = job.Hits[i];
            if (ct.IsCancellationRequested)
            {
                lock (gate) foreach (var rest in job.Hits.Skip(i)) queuedDirs.Remove(rest.ModDirectory);
                return;
            }
            lock (gate)
            {
                queuedDirs.Remove(hit.ModDirectory);
                status = $"Updating {hit.ModName}";
            }
            try
            {
                ConvertInstalled(job.Root, hit.ModDirectory, hit.ModName, "Updated", ct);
                lock (gate)
                    if (scan is not null)
                        scan = scan with { Hits = scan.Hits.Where(h => !string.Equals(h.ModDirectory, hit.ModDirectory, StringComparison.OrdinalIgnoreCase)).ToList() };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Fail(hit.ModName, "Could not update", ex);
            }
            lock (gate) done = i + 1;
        }
    }

    private void RunFile(FileJob job, CancellationToken ct)
    {
        var name = Path.GetFileName(job.Path);
        lock (gate) status = $"Converting {name}";
        try
        {
            var result = backend.ConvertFile(job.Path, options.TempRoot, ct);
            foreach (var note in result.Notes) log.Info($"{name}: {note}");
            if (!result.Changed || result.OutputPath is null)
            {
                Record(new ActivityEntry(clock.Now, "Convert", name, "Already Dawntrail-ready, nothing to convert"));
                return;
            }
            Record(new ActivityEntry(clock.Now, "Convert", name, $"Saved {Path.GetFileName(result.OutputPath)} and sent it to Penumbra"));
            Request(new HostRequest(HostRequestKind.Install, "", "", "", result.OutputPath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Fail(name, "Could not convert", ex);
        }
    }

    private void RunVerify(VerifyJob job)
    {
        PendingVerify? pending;
        lock (gate) awaitingVerify.TryGetValue(job.Dir, out pending);
        if (pending is null) return;
        var folder = ModPaths.Resolve(job.Root, job.Dir);
        try
        {
            if (backend.Verify(folder, pending.Data))
            {
                lock (gate) awaitingVerify.Remove(job.Dir);
                return;
            }
            if (pending.Reapplied)
            {
                lock (gate) awaitingVerify.Remove(job.Dir);
                Record(new ActivityEntry(clock.Now, "Update", job.Name, "Penumbra kept an older copy of this mod's file list. Scan to update it again.", true));
                return;
            }
            log.Warning($"{job.Name}: Penumbra saved over the updated file list; writing it again");
            backend.Reapply(folder, pending.Data);
            lock (gate) awaitingVerify[job.Dir] = pending with { Reapplied = true, At = clock.Now };
            Request(new HostRequest(HostRequestKind.Reload, job.Root, job.Dir, job.Name, folder));
        }
        catch (Exception ex)
        {
            lock (gate) awaitingVerify.Remove(job.Dir);
            Fail(job.Name, "Could not check the update", ex);
        }
    }

    private void ConvertInstalled(string root, string dir, string name, string what, CancellationToken ct)
    {
        var folder = ModPaths.Resolve(root, dir);
        var result = backend.ConvertFolder(folder, ct);
        foreach (var note in result.Notes) log.Info($"{name}: {note}");
        if (!result.Changed)
        {
            log.Info($"{name}: nothing to change");
            return;
        }
        log.Info($"{name}: {result.FilesWritten} files updated, {result.FilesAdded} added");
        Record(new ActivityEntry(clock.Now, "Update", name, $"{what}: {result.FilesWritten} files updated, {result.FilesAdded} added"));
        if (result.Data is not null)
            lock (gate)
            {
                // Forget checks Penumbra never got to (no reload happened); they hold file data.
                foreach (var stale in awaitingVerify.Where(kv => clock.Now - kv.Value.At > TimeSpan.FromMinutes(2)).Select(kv => kv.Key).ToList())
                    awaitingVerify.Remove(stale);
                awaitingVerify[dir] = new PendingVerify(result.Data, false, clock.Now);
            }
        Request(new HostRequest(HostRequestKind.Reload, root, dir, name, folder));
    }

    private void Request(HostRequest request)
    {
        lock (gate) hostRequests.Add(request);
        HostRequestsPending?.Invoke();
    }

    private void Fail(string mod, string what, Exception ex)
    {
        log.Error($"{mod}: {what}", ex);
        Record(new ActivityEntry(clock.Now, "Error", mod, $"{what}: {ex.Message}", true));
    }

    private void Record(ActivityEntry entry)
    {
        activity.Append(entry);
        lock (gate)
        {
            recent.Add(entry);
            if (recent.Count > options.RecentCount) recent.RemoveRange(0, recent.Count - options.RecentCount);
        }
    }
}
