// Ports: TexTools Mods/WizardData.cs (WritePmp, MakePagePrefix/MakeGroupPrefix/MakeOptionPrefix, ToPmpGroup),
// Mods/FileTypes/PMP.cs (PopulatePmpStandardOption, ResolvePMPBasePath, MakePMPPathSafe),
// Mods/FileTypes/PmpExtensions.cs (FileIdentifier.IdentifierListFromDictionaries, ResolveDuplicates),
// Helpers/IOUtil.cs (MakePathSafe, IsMetaInternalFile). Output is Penumbra FileVersion 3 layout, not TexTools' v4.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DawntrailReady.Core.Packaging;

/// <summary>Modpack-level fields and per-group settings for <see cref="ModpackFiles.WritePmp"/>.</summary>
public sealed class PmpMeta
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string Website { get; set; } = "";

    /// <summary>Per group name. Groups not listed are written as Single with the first option selected.</summary>
    public Dictionary<string, PmpGroupMeta> Groups { get; } = new(StringComparer.Ordinal);
}

public sealed class PmpGroupMeta
{
    /// <summary>"Single" or "Multi".</summary>
    public string Type { get; set; } = "Single";

    public string Description { get; set; } = "";
    public int Priority { get; set; }

    /// <summary>Selected index (Single) or bit mask (Multi).</summary>
    public ulong DefaultSettings { get; set; }

    /// <summary>Wizard page (index in the source's page list); more than one page puts files under <c>p{n}/</c>.</summary>
    public int Page { get; set; }

    public Dictionary<string, string> OptionDescriptions { get; } = new(StringComparer.Ordinal);
}

public static partial class ModpackFiles
{
    private static readonly HashSet<char> InvalidFileNameChars = [.. Path.GetInvalidFileNameChars()];

    private static readonly JsonSerializerOptions PmpJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class PmpGroup
    {
        public required string Name { get; init; }
        public required PmpGroupMeta Meta { get; init; }
        public List<ModOption> Options { get; } = [];
        public List<string> OptionPrefixes { get; } = [];
    }

    /// <summary>
    /// Writes a Penumbra-importable .pmp. Options are grouped by consecutive <see cref="ModOption.GroupName"/>; a group
    /// named "Default"/"Default Group" with one option named "Default"/"Default Option" becomes default_mod.json
    /// (WizardData.WritePmp's rule). Identical file bytes are stored once (under <c>common/N/</c>).
    /// </summary>
    public static void WritePmp(ModpackData data, PmpMeta meta, string outputPath)
    {
        var groups = new List<PmpGroup>();
        foreach (var opt in data.Options)
        {
            if (opt.Files is null)
                throw new InvalidDataException("Options without file data (IMC/Combining groups) cannot be written to a PMP.");
            if (groups.Count == 0 || groups[^1].Name != opt.GroupName.Trim())
            {
                var name = opt.GroupName.Trim();
                groups.Add(new PmpGroup { Name = name, Meta = meta.Groups.GetValueOrDefault(opt.GroupName) ?? meta.Groups.GetValueOrDefault(name) ?? new PmpGroupMeta() });
            }
            groups[^1].Options.Add(opt);
        }

        foreach (var g in groups)
        {
            if (string.IsNullOrWhiteSpace(g.Name) || g.Options.Any(o => string.IsNullOrWhiteSpace(o.OptionName)))
                throw new InvalidDataException("PMP Files must have valid group and option names.");
        }

        // Option folder prefixes (MakePagePrefix / MakeGroupPrefix / MakeOptionPrefix).
        var pages = groups.Select(g => g.Meta.Page).Distinct().OrderBy(p => p).ToList();
        var groupPrefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var pagePrefix = pages.Count > 1 ? "p" + (pages.IndexOf(g.Meta.Page) + 1) + "/" : "";
            var gName = MakePathSafe(g.Name);
            if (string.IsNullOrWhiteSpace(gName)) gName = "Blank Group";
            var groupPrefix = pagePrefix + gName + "/";
            // TexTools never increments i here and hangs on duplicate group names; we count up.
            for (var i = 1; !groupPrefixes.Add(groupPrefix); i++) groupPrefix = pagePrefix + gName + " (" + i + ")/";

            foreach (var o in g.Options)
            {
                var oName = MakePathSafe(o.OptionName);
                if (string.IsNullOrWhiteSpace(oName)) oName = "Blank Option";
                var path = g.Options.Count > 1 ? groupPrefix + oName + "/" : groupPrefix;
                for (var i = 1; g.OptionPrefixes.Contains(path); i++) path = groupPrefix + oName + " (" + i + ")/";
                g.OptionPrefixes.Add(path);
            }
        }

        // FileIdentifier.IdentifierListFromDictionaries + ResolveDuplicates: one zip path per distinct content.
        var zipFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var pmpPaths = ResolvePmpPaths(groups, zipFiles);

        var defaultGroup = groups.FirstOrDefault(g => (g.Name == "Default" || g.Name == "Default Group") && g.Options.Count == 1
            && (g.Options[0].OptionName == "Default" || g.Options[0].OptionName == "Default Option"));

        var jsons = new List<(string Name, JsonObject Json)>();
        var version = System.Version.TryParse(meta.Version, out var ver) ? ver : new Version("1.0");
        jsons.Add(("meta.json", new JsonObject
        {
            ["FileVersion"] = 3,
            ["Name"] = meta.Name,
            ["Author"] = meta.Author,
            ["Description"] = meta.Description,
            ["Image"] = "",
            ["Version"] = version.ToString(),
            ["Website"] = meta.Website,
            ["ModTags"] = new JsonArray(),
        }));

        var defaultJson = new JsonObject { ["Version"] = 0 };
        AddOptionData(defaultJson, defaultGroup is null ? null : pmpPaths[defaultGroup.Options[0]]);
        jsons.Add(("default_mod.json", defaultJson));

        var page = 0;
        var lastPage = int.MinValue;
        var groupIndex = 0;
        foreach (var g in groups)
        {
            if (g == defaultGroup) continue;
            if (lastPage != int.MinValue && g.Meta.Page != lastPage) page++;
            lastPage = g.Meta.Page;

            var isMulti = g.Meta.Type == "Multi";
            var options = new JsonArray();
            foreach (var o in g.Options)
            {
                var oj = new JsonObject
                {
                    ["Name"] = o.OptionName.Trim(),
                    ["Description"] = g.Meta.OptionDescriptions.GetValueOrDefault(o.OptionName) ?? "",
                };
                if (isMulti) oj["Priority"] = 0;
                AddOptionData(oj, pmpPaths[o]);
                options.Add(oj);
            }

            groupIndex++;
            jsons.Add(($"group_{groupIndex:000}_{MakePmpPathSafe(g.Name)}.json", new JsonObject
            {
                ["Version"] = 0,
                ["Name"] = g.Name,
                ["Description"] = g.Meta.Description,
                ["Image"] = "",
                ["Page"] = page,
                ["Priority"] = g.Meta.Priority,
                ["Type"] = isMulti ? "Multi" : "Single",
                ["DefaultSettings"] = g.Meta.DefaultSettings,
                ["Options"] = options,
            }));
        }

        var tmp = outputPath + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var (name, json) in jsons)
                    WriteEntry(zip, name, Encoding.UTF8.GetBytes(PenumbraJsonFile.UnescapeText(json.ToJsonString(PmpJsonOptions))));
                foreach (var (path, bytes) in zipFiles) WriteEntry(zip, path, bytes);
            }
            File.Move(tmp, outputPath, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>Per option: game path to zip path, in the option's file order. Fills <paramref name="zipFiles"/>.</summary>
    private static Dictionary<ModOption, List<(string GamePath, string ZipPath)>> ResolvePmpPaths(List<PmpGroup> groups,
        Dictionary<string, byte[]> zipFiles)
    {
        var ret = new Dictionary<ModOption, List<(string, string)>>();
        var seen = new Dictionary<string, (string Path, byte[] Bytes)>(StringComparer.Ordinal);
        var assigned = new List<(ModOption Option, string GamePath, string HashKey)>();
        var commonIdx = 1;

        foreach (var g in groups)
        {
            for (var oi = 0; oi < g.Options.Count; oi++)
            {
                var o = g.Options[oi];
                ret[o] = [];
                foreach (var (gamePath, source) in o.Files!)
                {
                    // PopulatePmpStandardOption never writes the meta tables themselves.
                    if (IsMetaInternalFile(gamePath)) continue;
                    byte[] bytes;
                    try
                    {
                        bytes = PenumbraModFolder.ReadBytes(source);
                    }
                    catch (Exception e) when (source is DiskFileSource && e is FileNotFoundException or DirectoryNotFoundException)
                    {
                        // "Sometimes poorly behaved penumbra folders don't actually have the files they claim they do."
                        continue;
                    }

                    var hashKey = Convert.ToHexString(SHA1.HashData(bytes));
                    var pmpPath = g.OptionPrefixes[oi] + gamePath;
                    if (seen.TryGetValue(hashKey, out var existing))
                    {
                        // Shift the target path into the common folder if we're used in multiple places.
                        if (!existing.Path.StartsWith("common/", StringComparison.Ordinal))
                        {
                            seen[hashKey] = ("common/" + commonIdx + "/" + Path.GetFileName(existing.Path), existing.Bytes);
                            commonIdx++;
                        }
                    }
                    else
                    {
                        seen.Add(hashKey, (pmpPath, bytes));
                    }
                    assigned.Add((o, gamePath, hashKey));
                }
            }
        }

        foreach (var (o, gamePath, hashKey) in assigned)
        {
            var (path, bytes) = seen[hashKey];
            zipFiles.TryAdd(path, bytes);
            ret[o].Add((gamePath, path));
        }
        return ret;
    }

    private static void AddOptionData(JsonObject option, List<(string GamePath, string ZipPath)>? files)
    {
        var fo = new JsonObject();
        if (files is not null)
        {
            // "Penumbra likes backslashes"
            foreach (var (gamePath, zipPath) in files) fo[gamePath] = zipPath.Replace("/", "\\");
        }
        option["Files"] = fo;
        option["FileSwaps"] = new JsonObject();
        option["Manipulations"] = new JsonArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(bytes);
    }

    /// <summary>
    /// Unzips a .pmp into <paramref name="tempFolder"/>, refusing (before extracting anything) any entry that would land
    /// outside it. Like TexTools' ResolvePMPBasePath, every extracted .tex gets EndwalkerUpgrade.FastValidateTexFile, and a
    /// pack whose meta.json sits in its only subfolder returns that subfolder.
    /// </summary>
    public static string ExtractPmp(string pmpPath, string tempFolder)
    {
        Directory.CreateDirectory(tempFolder);
        using (var zip = ZipFile.OpenRead(pmpPath))
        {
            var targets = zip.Entries.Select(e => (Entry: e, Full: ModPaths.Resolve(tempFolder, e.FullName))).ToList();
            foreach (var (entry, full) in targets)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(full);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                entry.ExtractToFile(full, true);
                if (full.EndsWith(".tex", StringComparison.Ordinal))
                {
                    try { OldTexFix.FastValidateTexFile(full); }
                    catch (Exception) { }
                }
            }
        }

        var path = tempFolder;
        if (!File.Exists(Path.Combine(path, "meta.json")))
        {
            var subs = Directory.EnumerateDirectories(path).ToList();
            if (subs.Count == 1 && File.Exists(Path.Combine(subs[0], "meta.json"))) path = Path.GetFullPath(subs[0]);
        }
        return path;
    }

    /// <summary>Zips a mod folder (meta.json at its root) into a .pmp, via <c>*.tmp</c> and a move.</summary>
    public static void ZipFolderAsPmp(string folder, string outputPath)
    {
        var tmp = outputPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        try
        {
            ZipFile.CreateFromDirectory(folder, tmp, CompressionLevel.Optimal, false);
            File.Move(tmp, outputPath, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>IOUtil.MakePathSafe(fileName) ('-' replacement, lower case, trimmed).</summary>
    private static string MakePathSafe(string fileName, char rep = '-', bool makeLowercase = true)
    {
        var ret = new StringBuilder(fileName.Length);
        foreach (var c in fileName)
        {
            if (InvalidFileNameChars.Contains(c)) ret.Append(rep);
            else ret.Append(makeLowercase ? char.ToLower(c) : c);
        }
        return ret.ToString().Trim();
    }

    /// <summary>PMP.MakePMPPathSafe: the naming Penumbra expects for group JSON files.</summary>
    private static string MakePmpPathSafe(string fileName)
    {
        if (fileName == ".") return "_";
        if (fileName == "..") return "__";
        return MakePathSafe(fileName.Normalize(NormalizationForm.FormKC), '_', true);
    }

    /// <summary>IOUtil.IsMetaInternalFile.</summary>
    private static bool IsMetaInternalFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".cmp" or ".eqp" or ".eqdp" or ".gmp" or ".est" or ".imc";
}
