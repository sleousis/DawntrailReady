using DawntrailReady.Core.Service;

namespace DawntrailReady.Core.Tests.Service;

public sealed class ActivityLogTests
{
    private sealed class MemoryFile : IAppendOnlyFile
    {
        public string? Text { get; set; }
        public bool Unreadable { get; set; }
        public bool Exists => Text is not null;
        public string ReadAll() => Unreadable ? throw new IOException("locked") : Text!;
        public bool EndsWithNewline() => Text is null || Text.Length == 0 || Text[^1] == '\n';
        public void Append(string text) => Text = (Text ?? "") + text;
    }

    private static ActivityEntry Entry(string mod) => new(DateTimeOffset.UnixEpoch, "Update", mod, "done");

    [Fact]
    public void Entries_come_back_newest_last()
    {
        var log = new JsonLinesActivityLog(new MemoryFile());
        log.Append(Entry("A"));
        log.Append(Entry("B"));
        log.Append(Entry("C"));
        Assert.Equal(["B", "C"], log.ReadRecent(2).Select(e => e.Mod));
    }

    [Fact]
    public void A_torn_last_line_is_skipped_and_does_not_swallow_the_next_entry()
    {
        var file = new MemoryFile { Text = "{\"At\":\"1970-01-01T00:00:00+00:00\",\"Kind\":\"Upd" };
        var log = new JsonLinesActivityLog(file);
        log.Append(Entry("A"));
        Assert.Equal(["A"], log.ReadRecent(10).Select(e => e.Mod));
    }

    [Fact]
    public void An_unreadable_history_is_never_overwritten()
    {
        var file = new MemoryFile { Text = "old line\n", Unreadable = true };
        var log = new JsonLinesActivityLog(file);
        Assert.Empty(log.ReadRecent(10));
        log.Append(Entry("A"));
        Assert.StartsWith("old line\n", file.Text);
    }

    [Fact]
    public void A_failed_write_is_counted_not_thrown()
    {
        var log = new JsonLinesActivityLog(new ThrowingFile());
        log.Append(Entry("A"));
        Assert.Equal(1, log.Failures);
    }

    private sealed class ThrowingFile : IAppendOnlyFile
    {
        public bool Exists => false;
        public string ReadAll() => throw new IOException();
        public bool EndsWithNewline() => true;
        public void Append(string text) => throw new IOException("disk full");
    }
}
