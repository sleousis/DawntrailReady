namespace DawntrailReady.Core.Packaging;

/// <summary>
/// All-or-nothing file writes: each write goes to <c>*.tmp</c> then <c>File.Move(overwrite)</c>. On
/// <see cref="Rollback"/> every replaced file gets its original bytes back, and new files and new folders are removed.
/// </summary>
internal sealed class AtomicFileWriter
{
    private readonly Dictionary<string, byte[]> _originals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _created = [];
    private readonly List<string> _createdDirs = [];
    private readonly HashSet<string> _tmps = new(StringComparer.OrdinalIgnoreCase);

    public int Replaced => _originals.Count;
    public int Created => _created.Count;

    public void Write(string fullPath, byte[] bytes)
    {
        if (File.Exists(fullPath))
        {
            if (!_originals.ContainsKey(fullPath) && !_created.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                _originals.Add(fullPath, File.ReadAllBytes(fullPath));
        }
        else
        {
            CreateDirectories(Path.GetDirectoryName(fullPath)!);
            _created.Add(fullPath);
        }

        var tmp = fullPath + ".tmp";
        _tmps.Add(tmp);
        WriteDurably(tmp, bytes);
        File.Move(tmp, fullPath, true);
        _tmps.Remove(tmp);
    }

    /// <summary>
    /// Writes the bytes all the way to the disk before returning. Only then does the rename replace the original, so
    /// a system crash leaves either the old file or the new one, never a file of zeros from an unflushed write.
    /// </summary>
    internal static void WriteDurably(string path, byte[] bytes)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough);
        fs.Write(bytes);
        fs.Flush(flushToDisk: true);
    }

    public void Rollback()
    {
        foreach (var tmp in _tmps) TryDo(() => File.Delete(tmp));
        foreach (var (path, bytes) in _originals) TryDo(() => File.WriteAllBytes(path, bytes));
        foreach (var path in _created) TryDo(() => File.Delete(path));
        for (var i = _createdDirs.Count - 1; i >= 0; i--)
        {
            var dir = _createdDirs[i];
            TryDo(() =>
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            });
        }
    }

    private void CreateDirectories(string dir)
    {
        var missing = new Stack<string>();
        for (var d = dir; !string.IsNullOrEmpty(d) && !Directory.Exists(d); d = Path.GetDirectoryName(d)) missing.Push(d);
        while (missing.Count > 0)
        {
            var d = missing.Pop();
            Directory.CreateDirectory(d);
            _createdDirs.Add(d);
        }
    }

    private static void TryDo(Action act)
    {
        try { act(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
