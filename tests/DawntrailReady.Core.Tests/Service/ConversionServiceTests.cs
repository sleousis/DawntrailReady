using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Service;

namespace DawntrailReady.Core.Tests.Service;

public sealed class ConversionServiceTests
{
    private const string Root = @"C:\mods";

    private sealed class FakeClock : IServiceClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        public List<TimeSpan> Sleeps { get; } = [];
        public void Sleep(TimeSpan duration, CancellationToken ct) { Sleeps.Add(duration); Now += duration; }
    }

    private sealed class NullLog : IServiceLog
    {
        public List<string> Lines { get; } = [];
        public void Info(string message) => Lines.Add(message);
        public void Warning(string message, Exception? ex = null) => Lines.Add(message);
        public void Error(string message, Exception? ex = null) => Lines.Add(message);
    }

    private sealed class MemoryActivity : IActivityLog
    {
        public List<ActivityEntry> Entries { get; } = [];
        public int Failures => 0;
        public void Append(ActivityEntry entry) => Entries.Add(entry);
        public IReadOnlyList<ActivityEntry> ReadRecent(int max) => Entries.TakeLast(max).ToList();
    }

    private sealed class FakeBackend : IConversionBackend
    {
        public HashSet<string> Old { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Throws { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Converted { get; } = [];
        public List<string> Reapplied { get; } = [];
        public Queue<bool> VerifyAnswers { get; } = new();
        public FileConversion FileResult { get; set; } = new(true, @"C:\dl\Mod (Dawntrail).pmp", []);

        private static string Name(string folder) => Path.GetFileName(folder);

        public ModCheck Check(string modFolder)
        {
            if (Throws.Contains(Name(modFolder))) throw new IOException("locked");
            return new ModCheck(Old.Contains(Name(modFolder)), 2, 1);
        }

        public FolderConversion ConvertFolder(string modFolder, CancellationToken ct)
        {
            if (Throws.Contains(Name(modFolder))) throw new InvalidDataException("broken");
            Converted.Add(Name(modFolder));
            var changed = Old.Remove(Name(modFolder));
            return new FolderConversion(changed, 3, 1, [], changed ? new ModpackData() : null);
        }

        public bool Verify(string modFolder, ModpackData expected) => VerifyAnswers.Count == 0 || VerifyAnswers.Dequeue();
        public void Reapply(string modFolder, ModpackData expected) => Reapplied.Add(Name(modFolder));
        public FileConversion ConvertFile(string path, string tempRoot, CancellationToken ct) => FileResult;
    }

    private readonly FakeClock clock = new();
    private readonly FakeBackend backend = new();
    private readonly MemoryActivity activity = new();
    private readonly NullLog log = new();

    private ConversionService Service() => new(backend, clock, activity, log, new ServiceOptions(), startWorker: false);

    private static void Drain(ConversionService s) { while (s.RunOnce()) { } }

    [Fact]
    public void An_old_import_waits_to_settle_then_is_updated_and_reloaded()
    {
        backend.Old.Add("Old Hat");
        using var s = Service();
        s.EnqueueImport(Root, "Old Hat", "Old Hat");
        Drain(s);

        Assert.Equal(TimeSpan.FromSeconds(2), clock.Sleeps.Single());
        Assert.Equal(["Old Hat"], backend.Converted);
        var req = Assert.Single(s.DrainHostRequests());
        Assert.Equal(HostRequestKind.Reload, req.Kind);
        Assert.Equal("Old Hat", req.ModDirectory);
    }

    [Fact]
    public void A_dawntrail_import_is_left_alone()
    {
        using var s = Service();
        s.EnqueueImport(Root, "New Hat", "New Hat");
        Drain(s);

        // The backend is asked (its header check says "nothing to do"); nothing is reloaded or recorded.
        Assert.Equal(["New Hat"], backend.Converted);
        Assert.Empty(s.DrainHostRequests());
        Assert.Empty(activity.Entries);
    }

    [Fact]
    public void The_same_mod_is_queued_once()
    {
        backend.Old.Add("Old Hat");
        using var s = Service();
        Assert.True(s.EnqueueImport(Root, "Old Hat", "Old Hat"));
        Assert.False(s.EnqueueImport(Root, "Old Hat", "Old Hat"));
        Drain(s);
        Assert.Single(backend.Converted);
    }

    [Fact]
    public void A_scan_lists_only_mods_that_need_updating()
    {
        backend.Old.UnionWith(["A", "C"]);
        backend.Throws.Add("D");
        using var s = Service();
        s.EnqueueScan(Root, new Dictionary<string, string> { ["A"] = "A", ["B"] = "B", ["C"] = "C", ["D"] = "D" });
        Drain(s);

        var scan = s.Snapshot.Scan!;
        Assert.False(scan.Running);
        Assert.Equal(4, scan.Checked);
        Assert.Equal(1, scan.Failed);
        Assert.Equal(["A", "C"], scan.Hits.Select(h => h.ModDirectory));
        Assert.Empty(backend.Converted);
    }

    [Fact]
    public void Update_all_does_exactly_the_selection_and_reloads_each()
    {
        backend.Old.UnionWith(["A", "B", "C"]);
        using var s = Service();
        s.EnqueueScan(Root, new Dictionary<string, string> { ["A"] = "A", ["B"] = "B", ["C"] = "C" });
        Drain(s);
        var selection = s.Snapshot.Scan!.Hits.Where(h => h.ModDirectory != "B").ToList();

        s.EnqueueFixAll(Root, selection);
        Drain(s);

        Assert.Equal(["A", "C"], backend.Converted);
        Assert.Equal(["A", "C"], s.DrainHostRequests().Select(r => r.ModDirectory));
        Assert.Equal(["B"], s.Snapshot.Scan!.Hits.Select(h => h.ModDirectory));
    }

    [Fact]
    public void One_broken_mod_does_not_stop_the_rest()
    {
        backend.Old.UnionWith(["A", "B"]);
        backend.Throws.Add("A");
        using var s = Service();
        s.EnqueueFixAll(Root, [new ScanHit("A", "A", 1, 0), new ScanHit("B", "B", 1, 0)]);
        Drain(s);

        Assert.Equal(["B"], backend.Converted);
        Assert.Contains(activity.Entries, e => e.Failed && e.Mod == "A");
    }

    [Fact]
    public void Lost_changes_are_written_again_once_then_reported()
    {
        backend.Old.Add("Hat");
        backend.VerifyAnswers.Enqueue(false);
        backend.VerifyAnswers.Enqueue(false);
        using var s = Service();
        s.EnqueueImport(Root, "Hat", "Hat");
        Drain(s);
        s.DrainHostRequests();

        s.ReportReloaded(Root, "Hat", "Hat");
        Drain(s);
        Assert.Equal(["Hat"], backend.Reapplied);
        Assert.Single(s.DrainHostRequests());

        s.ReportReloaded(Root, "Hat", "Hat");
        Drain(s);
        Assert.Equal(["Hat"], backend.Reapplied);
        Assert.Empty(s.DrainHostRequests());
        Assert.Contains(activity.Entries, e => e.Failed && e.Mod == "Hat");

        s.ReportReloaded(Root, "Hat", "Hat");
        Assert.False(s.RunOnce());
    }

    [Fact]
    public void Kept_changes_need_nothing_more()
    {
        backend.Old.Add("Hat");
        using var s = Service();
        s.EnqueueImport(Root, "Hat", "Hat");
        Drain(s);
        s.DrainHostRequests();
        s.ReportReloaded(Root, "Hat", "Hat");
        Drain(s);

        Assert.Empty(backend.Reapplied);
        Assert.Empty(s.DrainHostRequests());
    }

    [Fact]
    public void A_converted_file_is_sent_to_penumbra()
    {
        using var s = Service();
        s.EnqueueConvertFile(@"C:\dl\Mod.ttmp2");
        Drain(s);

        var req = Assert.Single(s.DrainHostRequests());
        Assert.Equal(HostRequestKind.Install, req.Kind);
        Assert.Equal(@"C:\dl\Mod (Dawntrail).pmp", req.Path);
    }

    [Fact]
    public void A_file_that_needs_nothing_is_not_installed()
    {
        backend.FileResult = new FileConversion(false, null, []);
        using var s = Service();
        s.EnqueueConvertFile(@"C:\dl\Mod.pmp");
        Drain(s);

        Assert.Empty(s.DrainHostRequests());
        Assert.Contains(activity.Entries, e => e.Detail.Contains("Already Dawntrail-ready"));
    }

    [Fact]
    public void A_mod_folder_outside_the_root_is_refused()
    {
        backend.Old.Add("x");
        using var s = Service();
        s.EnqueueImport(Root, @"..\..\Windows", "x");
        Drain(s);

        Assert.Empty(backend.Converted);
        Assert.Contains(activity.Entries, e => e.Failed);
    }

    [Fact]
    public void The_worker_thread_processes_jobs_and_stops_on_dispose()
    {
        backend.Old.Add("Hat");
        var pending = new ManualResetEventSlim();
        var s = new ConversionService(backend, clock, activity, log, new ServiceOptions(), startWorker: true);
        s.HostRequestsPending += pending.Set;
        s.EnqueueImport(Root, "Hat", "Hat");

        Assert.True(pending.Wait(TimeSpan.FromSeconds(10)));
        s.Dispose();
        Assert.False(s.EnqueueImport(Root, "Other", "Other"));
    }
}
