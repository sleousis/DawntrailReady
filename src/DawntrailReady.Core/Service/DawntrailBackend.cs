using DawntrailReady.Core.Game;
using DawntrailReady.Core.Materials;
using DawntrailReady.Core.Packaging;
using DawntrailReady.Core.Upgrade;

namespace DawntrailReady.Core.Service;

/// <summary>
/// The real file work: Penumbra mod folders and mod files in, TexTools' Dawntrail upgrade in the middle.
/// Whether a mod needs upgrading is decided by <see cref="PreDawntrailDetector"/> (does it hold anything made for
/// the game before Dawntrail?); how it is upgraded belongs entirely to <see cref="ModpackUpgrader"/>.
/// Mods are read with the game data so FileSwaps are listed the way TexTools lists them when it loads a pack.
/// </summary>
public sealed class DawntrailBackend(IGameData game) : IConversionBackend
{
    private readonly ModpackUpgrader upgrader = new(game);

    public ModCheck Check(string modFolder)
    {
        var data = PenumbraModFolder.Read(modFolder, game);
        var report = PreDawntrailDetector.Inspect(data, game);
        if (!report.Any) return new ModCheck(false, 0, 0);

        // The upgrade runs in memory here and nothing is written. If TexTools' upgrade can't handle the mod, the
        // exception reaches the scan, which reports it.
        if (!Upgrade(data, CancellationToken.None).Real) return new ModCheck(false, 0, 0);
        return new ModCheck(true, report.LegacyMaterials, report.OldModels);
    }

    public FolderConversion ConvertFolder(string modFolder, CancellationToken ct)
    {
        var data = PenumbraModFolder.Read(modFolder, game);
        // A Dawntrail-ready mod costs a few small reads and nothing else.
        if (!PreDawntrailDetector.Inspect(data, game).Any) return new FolderConversion(false, 0, 0, [], null);
        var (result, real) = Upgrade(data, ct);
        if (!real) return new FolderConversion(false, 0, 0, result.Notes, null);

        // Last point where stopping is allowed: nothing has been written yet.
        ct.ThrowIfCancellationRequested();
        var written = PenumbraModFolder.WriteChanges(modFolder, data);
        return new FolderConversion(true, written.FilesWritten, written.FilesAdded, Distinct(result.Notes, written.Warnings), data);
    }

    /// <summary>After Penumbra's reload: are all the files we wrote still listed in the mod's JSON?</summary>
    public bool Verify(string modFolder, ModpackData expected)
    {
        var now = PenumbraModFolder.Read(modFolder, game);
        var byKey = now.Options.ToDictionary(o => o.Key, StringComparer.Ordinal);
        foreach (var option in expected.FileOptions)
        {
            if (!byKey.TryGetValue(option.Key, out var current) || current.Files is null) return false;
            // File swaps (and aliases of them) are never written as files, so they are not ours to find.
            if (option.Files!.Where(kv => kv.Value is not PenumbraFileSwapSource).Any(kv => !current.Files.ContainsKey(kv.Key))) return false;
        }
        return true;
    }

    public void Reapply(string modFolder, ModpackData expected) => PenumbraModFolder.WriteChanges(modFolder, expected);

    public FileConversion ConvertFile(string path, string tempRoot, CancellationToken ct)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var work = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            if (ext == ".pmp")
            {
                var folder = ModpackFiles.ExtractPmp(path, work);
                var data = PenumbraModFolder.Read(folder, game);
                if (!PreDawntrailDetector.Inspect(data, game).Any) return new FileConversion(false, null, []);
                var (result, real) = Upgrade(data, ct);
                if (!real) return new FileConversion(false, null, result.Notes);
                ct.ThrowIfCancellationRequested();
                var written = PenumbraModFolder.WriteChanges(folder, data);
                var output = OutputPathFor(path);
                ModpackFiles.ZipFolderAsPmp(folder, output);
                return new FileConversion(true, output, Distinct(result.Notes, written.Warnings));
            }

            if (ext is ".ttmp2" or ".ttmp")
            {
                // Disposing removes the pack's extracted data blob from the temp folder.
                using var pack = ModpackFiles.ReadTtmp(path);
                if (!PreDawntrailDetector.Inspect(pack.Data, game).Any) return new FileConversion(false, null, []);
                var (result, real) = Upgrade(pack.Data, ct);
                if (!real) return new FileConversion(false, null, Distinct(pack.Warnings, result.Notes));
                ct.ThrowIfCancellationRequested();
                var output = OutputPathFor(path);
                ModpackFiles.WritePmp(pack.Data, pack.Meta, output);
                return new FileConversion(true, output, Distinct(pack.Warnings, result.Notes));
            }

            throw new InvalidDataException($"Not a mod file: {Path.GetFileName(path)}");
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Runs TexTools' upgrade on <paramref name="data"/> (in memory) and says whether it really changed the mod.</summary>
    private (UpgradeResult Result, bool Real) Upgrade(ModpackData data, CancellationToken ct)
    {
        var before = Snapshot(data);
        var result = upgrader.UpgradeModpack(data, true, ct).GetAwaiter().GetResult();
        return (result, result.AnyChanges && HasRealChanges(before, result));
    }

    /// <summary>Every option's entries before the upgrade, so replaced files can be compared with their originals.</summary>
    public static Dictionary<ModOption, Dictionary<string, FileSource>> Snapshot(ModpackData data) =>
        data.FileOptions.ToDictionary(o => o, o => new Dictionary<string, FileSource>(o.Files!, StringComparer.Ordinal));

    /// <summary>
    /// Whether the upgrade changed the mod in substance. TexTools re-saves some materials it touches without
    /// changing them (hair "mashup" repathing rewrites every hair material), and its material writer normalises
    /// padding and the dye flag. A mod whose only differences are those re-saves is left alone. Anything else (a file
    /// added, a model or texture changed, a material that differs from its own re-save) means the upgrade did its
    /// job, and then TexTools' output is written in full.
    /// Entries pointing at a file swap are ignored: they are never written, so they change nothing on disk.
    /// </summary>
    public static bool HasRealChanges(Dictionary<ModOption, Dictionary<string, FileSource>> before, UpgradeResult result)
    {
        foreach (var change in result.Changes)
        {
            if (change.Option.Files is null || !change.Option.Files.TryGetValue(change.GamePath, out var now)) continue;
            if (now is PenumbraFileSwapSource) continue;
            if (change.Added) return true;
            if (!change.ContentChanged) continue;
            if (!change.GamePath.EndsWith(".mtrl", StringComparison.Ordinal)) return true;
            if (!before.TryGetValue(change.Option, out var old) || !old.TryGetValue(change.GamePath, out var original)) return true;

            try
            {
                var resaved = Mtrl.XivMtrlToUncompressedMtrl(Mtrl.GetXivMtrl(original.Read(), change.GamePath));
                if (!resaved.AsSpan().SequenceEqual(now.Read())) return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An original TexTools can't even re-save is not "just normalised".
                return true;
            }
        }
        return false;
    }

    /// <summary>"Name (Dawntrail).pmp" next to the original; never replaces an existing file.</summary>
    public static string OutputPathFor(string inputPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(dir, $"{name} (Dawntrail).pmp");
        for (var n = 2; File.Exists(candidate); n++)
            candidate = Path.Combine(dir, $"{name} (Dawntrail) ({n}).pmp");
        return candidate;
    }

    /// <summary>The same note from several options (e.g. one model used by three body types) is shown once.</summary>
    private static List<string> Distinct(IEnumerable<string> a, IEnumerable<string> b) => a.Concat(b).Distinct(StringComparer.Ordinal).ToList();
}
