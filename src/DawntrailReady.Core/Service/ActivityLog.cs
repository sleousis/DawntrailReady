using System.Text;
using System.Text.Json;

namespace DawntrailReady.Core.Service;

/// <summary>One line of what the plugin did. Kept short: it is shown in the window's recent list.</summary>
public sealed record ActivityEntry(DateTimeOffset At, string Kind, string Mod, string Detail, bool Failed = false);

public interface IActivityLog
{
    /// <summary>Never throws: a history that cannot be written must not fail the work it records.</summary>
    void Append(ActivityEntry entry);

    IReadOnlyList<ActivityEntry> ReadRecent(int max);

    int Failures { get; }
}

/// <summary>A text file that is only ever appended to. The plugin backs it with a real file.</summary>
public interface IAppendOnlyFile
{
    bool Exists { get; }
    string ReadAll();
    bool EndsWithNewline();
    void Append(string text);
}

/// <summary>
/// Append-only JSON lines. A torn or unknown line is skipped when reading. The file is never rewritten, so an
/// unreadable history can't be overwritten with an empty one.
/// </summary>
public sealed class JsonLinesActivityLog(IAppendOnlyFile file) : IActivityLog
{
    private readonly Lock gate = new();
    private int failures;

    public int Failures => Volatile.Read(ref failures);

    public void Append(ActivityEntry entry)
    {
        lock (gate)
        {
            try
            {
                var line = JsonSerializer.Serialize(entry);
                var sb = new StringBuilder();
                // A crash can leave a torn last line; start on a fresh one so it doesn't swallow this entry.
                if (file.Exists && !file.EndsWithNewline()) sb.Append('\n');
                sb.Append(line).Append('\n');
                file.Append(sb.ToString());
            }
            catch (Exception)
            {
                failures++;
            }
        }
    }

    public IReadOnlyList<ActivityEntry> ReadRecent(int max)
    {
        lock (gate)
        {
            string text;
            try
            {
                if (!file.Exists) return [];
                text = file.ReadAll();
            }
            catch (Exception)
            {
                return [];
            }

            var result = new List<ActivityEntry>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                try
                {
                    if (JsonSerializer.Deserialize<ActivityEntry>(line) is { } e) result.Add(e);
                }
                catch (Exception e) when (e is JsonException or NotSupportedException)
                {
                    // torn or foreign line: skip
                }
            }
            return result.Count <= max ? result : result.GetRange(result.Count - max, max);
        }
    }
}

/// <summary>The real append-only file.</summary>
public sealed class DiskAppendOnlyFile(string path) : IAppendOnlyFile
{
    public bool Exists => File.Exists(path);

    public string ReadAll() => File.ReadAllText(path);

    public bool EndsWithNewline()
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return true;
        fs.Seek(-1, SeekOrigin.End);
        return fs.ReadByte() == '\n';
    }

    public void Append(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Flushed to the disk, so a system crash can't leave the history ending in zeros.
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        fs.Write(System.Text.Encoding.UTF8.GetBytes(text));
        fs.Flush(flushToDisk: true);
    }
}
