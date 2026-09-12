// No TexTools counterpart: TexTools re-exports a whole new modpack (WizardData.WritePmp), which drops FileSwaps,
// images and unknown JSON. This writes only what the upgrade changed back into the existing Penumbra folder.
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DawntrailReady.Core.Packaging;

/// <summary>What <see cref="PenumbraModFolder.WriteChanges"/> did.</summary>
/// <param name="FilesWritten">Existing mod files overwritten in place.</param>
/// <param name="FilesAdded">New files created (sibling <c>_dt</c> files or files for new game paths).</param>
/// <param name="JsonChanged">Whether any JSON file was rewritten.</param>
public sealed record ModFolderWriteResult(int FilesWritten, int FilesAdded, bool JsonChanged, IReadOnlyList<string> Warnings);

public static partial class PenumbraModFolder
{
    private sealed class Change
    {
        public required PenumbraContainer Container { get; init; }
        public required string GamePath { get; init; }
        public string? OriginalRel { get; init; }
        public string? DiskRel { get; init; }
        public byte[]? Bytes { get; init; }
    }

    /// <summary>
    /// Writes back every option whose Files differ from the folder's JSON. All or nothing: on any failure every
    /// file replaced so far is restored and every new file deleted.
    /// </summary>
    public static ModFolderWriteResult WriteChanges(string modFolder, ModpackData data)
    {
        var root = Path.GetFullPath(modFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var json = PenumbraModJson.Load(modFolder);
        var warnings = new List<string>();
        var containers = json.Containers.ToDictionary(c => c.Key, StringComparer.Ordinal);

        var original = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var c in json.Containers.Where(c => c.HasStandardData))
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (gp, rel) in PenumbraModJson.FileEntries(c.Option))
            {
                if (PenumbraModJson.CanImport(gp)) map[gp] = rel;
            }
            original[c.Key] = map;
        }

        // 1. Collect changes (reading every new byte array before anything is written).
        var changes = new List<Change>();
        var removals = new List<(PenumbraContainer Container, string GamePath)>();
        foreach (var opt in data.Options)
        {
            if (opt.Files is null) continue;
            if (!containers.TryGetValue(opt.Key, out var c) || !c.HasStandardData)
            {
                warnings.Add($"Option '{opt.GroupName} / {opt.OptionName}' is not in the mod's JSON; left unchanged.");
                continue;
            }

            var orig = original[c.Key];
            foreach (var (gp, src) in opt.Files)
            {
                if (src is PenumbraFileSwapSource) continue;
                var origRel = orig.GetValueOrDefault(gp);
                var diskRel = DiskRelativePath(src, root);
                if (origRel is not null && diskRel is not null && NormalizeRelative(diskRel) == NormalizeRelative(origRel)) continue;

                if (diskRel is not null) ModPaths.Resolve(root, diskRel);
                changes.Add(new Change
                {
                    Container = c, GamePath = gp, OriginalRel = origRel, DiskRel = diskRel,
                    Bytes = diskRel is null ? ReadBytes(src) : null,
                });
            }
            foreach (var gp in orig.Keys.Where(k => !opt.Files.ContainsKey(k))) removals.Add((c, gp));
        }

        if (changes.Count == 0 && removals.Count == 0) return new ModFolderWriteResult(0, 0, false, warnings);

        // 2. Which files must keep their current bytes: anything any JSON "Files" entry still points at afterwards.
        var replaced = new HashSet<(JsonObject, string)>();
        foreach (var ch in changes) replaced.Add((FilesObject(ch.Container, false)!, ch.GamePath));
        foreach (var (c, gp) in removals) replaced.Add((FilesObject(c, false)!, gp));

        var keepOld = new HashSet<string>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in json.Files)
        {
            foreach (var (filesObj, gp, rel) in AllFileRefs(file.Root))
            {
                referenced.Add(NormalizeRelative(rel));
                if (!replaced.Contains((filesObj, gp))) keepOld.Add(NormalizeRelative(rel));
            }
        }
        foreach (var ch in changes.Where(x => x.DiskRel is not null)) keepOld.Add(NormalizeRelative(ch.DiskRel!));

        // 3. In place when nothing else needs the old bytes and every replacement of that file agrees.
        var inPlace = new Dictionary<string, (string Rel, byte[] Bytes)>(StringComparer.Ordinal);
        foreach (var grp in changes.Where(x => x.Bytes is not null && x.OriginalRel is not null)
                     .GroupBy(x => NormalizeRelative(x.OriginalRel!)))
        {
            var first = grp.First().Bytes!;
            if (!keepOld.Contains(grp.Key) && grp.All(x => x.Bytes!.AsSpan().SequenceEqual(first)))
                inPlace[grp.Key] = (grp.First().OriginalRel!, first);
        }

        // 4. Everything else goes to new files, identical bytes sharing one file.
        var dedupe = new Dictionary<string, List<(string Rel, byte[] Bytes)>>(StringComparer.Ordinal);
        foreach (var (rel, bytes) in inPlace.Values) Register(dedupe, rel, bytes);

        var planned = new HashSet<string>(StringComparer.Ordinal);
        var newFiles = new List<(string Rel, byte[] Bytes)>();
        bool Taken(string rel)
        {
            var n = NormalizeRelative(rel);
            if (referenced.Contains(n) || planned.Contains(n)) return true;
            var full = ModPaths.Resolve(root, rel);
            return File.Exists(full) || Directory.Exists(full);
        }

        foreach (var ch in changes)
        {
            var filesObj = FilesObject(ch.Container, true)!;
            string target;
            if (ch.DiskRel is not null)
            {
                target = ch.DiskRel;
            }
            else if (ch.OriginalRel is not null && inPlace.ContainsKey(NormalizeRelative(ch.OriginalRel)))
            {
                continue;
            }
            else if (FindDuplicate(dedupe, ch.Bytes!) is { } dup)
            {
                target = dup;
            }
            else
            {
                var candidate = ch.OriginalRel ?? NewPathFor(original[ch.Container.Key], ch.GamePath);
                target = ch.OriginalRel is null && !Taken(candidate) ? candidate : Sibling(candidate, Taken);
                ModPaths.Resolve(root, target);
                planned.Add(NormalizeRelative(target));
                newFiles.Add((target, ch.Bytes!));
                Register(dedupe, target, ch.Bytes!);
            }

            filesObj[ch.GamePath] = JsonValue.Create(target.Replace('/', '\\'));
            ch.Container.File.Dirty = true;
        }

        foreach (var (c, gp) in removals)
        {
            FilesObject(c, false)?.Remove(gp);
            c.File.Dirty = true;
        }

        // 5. Write: files first, JSON last, all or nothing.
        var writer = new AtomicFileWriter();
        var written = 0;
        try
        {
            foreach (var (rel, bytes) in inPlace.Values)
            {
                var full = ModPaths.Resolve(root, rel);
                if (File.Exists(full) && File.ReadAllBytes(full).AsSpan().SequenceEqual(bytes)) continue;
                writer.Write(full, bytes);
                written++;
            }
            foreach (var (rel, bytes) in newFiles) writer.Write(ModPaths.Resolve(root, rel), bytes);
            foreach (var file in json.Files.Where(f => f.Dirty)) writer.Write(file.FullPath, file.ToBytes());
        }
        catch
        {
            writer.Rollback();
            throw;
        }

        return new ModFolderWriteResult(written, newFiles.Count, json.Files.Any(f => f.Dirty), warnings);
    }

    /// <summary>A source's bytes for hashing/writing only (never modified): MemoryFileSource's array without a copy.</summary>
    internal static byte[] ReadBytes(FileSource src) => src is MemoryFileSource m ? m.Data : src.Read();

    /// <summary>The source's path relative to this mod folder, or null when its bytes have to be written.</summary>
    private static string? DiskRelativePath(FileSource src, string root)
    {
        if (src is not DiskFileSource d) return null;
        var folder = Path.GetFullPath(d.ModFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(folder, root, StringComparison.OrdinalIgnoreCase) ? d.RelativePath : null;
    }

    private static JsonObject? FilesObject(PenumbraContainer c, bool create)
    {
        if (c.Option["Files"] is JsonObject o) return o;
        if (!create) return null;
        o = new JsonObject();
        c.Option["Files"] = o;
        return o;
    }

    private static IEnumerable<(JsonObject FilesObj, string GamePath, string Rel)> AllFileRefs(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    if (kv.Key == "Files" && kv.Value is JsonObject files)
                    {
                        foreach (var f in files)
                        {
                            var rel = PenumbraModJson.Str(f.Value);
                            if (rel.Length > 0) yield return (files, f.Key, rel);
                        }
                    }
                    else
                    {
                        foreach (var r in AllFileRefs(kv.Value)) yield return r;
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    foreach (var r in AllFileRefs(item)) yield return r;
                break;
        }
    }

    /// <summary>Next to the option's other files: the prefix its files use in front of their game paths.</summary>
    private static string NewPathFor(Dictionary<string, string> optionFiles, string gamePath)
    {
        var gp = gamePath.Replace('\\', '/');
        foreach (var (key, rel) in optionFiles)
        {
            var k = key.Replace('\\', '/');
            var r = rel.Replace('\\', '/');
            if (r.Length >= k.Length && r.EndsWith(k, StringComparison.OrdinalIgnoreCase)
                && (r.Length == k.Length || r[r.Length - k.Length - 1] == '/'))
            {
                return (r[..^k.Length] + gp).Replace('/', '\\');
            }
        }

        if (optionFiles.Count > 0)
        {
            var first = optionFiles.Values.First().Replace('/', '\\');
            var slash = first.LastIndexOf('\\');
            var name = gp[(gp.LastIndexOf('/') + 1)..];
            return slash < 0 ? name : first[..(slash + 1)] + name;
        }
        return gp.Replace('/', '\\');
    }

    /// <summary><c>name_dt.ext</c>, <c>name_dt2.ext</c>, ... beside <paramref name="rel"/>.</summary>
    private static string Sibling(string rel, Func<string, bool> taken)
    {
        rel = rel.Replace('/', '\\');
        var slash = rel.LastIndexOf('\\');
        var dir = slash < 0 ? "" : rel[..(slash + 1)];
        var name = rel[(slash + 1)..];
        var dot = name.LastIndexOf('.');
        var stem = dot <= 0 ? name : name[..dot];
        var ext = dot <= 0 ? "" : name[dot..];
        for (var i = 1; ; i++)
        {
            var candidate = dir + stem + (i == 1 ? "_dt" : "_dt" + i) + ext;
            if (!taken(candidate)) return candidate;
        }
    }

    private static void Register(Dictionary<string, List<(string Rel, byte[] Bytes)>> dedupe, string rel, byte[] bytes)
    {
        var key = Convert.ToHexString(SHA256.HashData(bytes));
        if (!dedupe.TryGetValue(key, out var list)) dedupe[key] = list = [];
        list.Add((rel, bytes));
    }

    private static string? FindDuplicate(Dictionary<string, List<(string Rel, byte[] Bytes)>> dedupe, byte[] bytes)
    {
        if (!dedupe.TryGetValue(Convert.ToHexString(SHA256.HashData(bytes)), out var list)) return null;
        foreach (var (rel, b) in list)
        {
            if (b.AsSpan().SequenceEqual(bytes)) return rel;
        }
        return null;
    }
}
