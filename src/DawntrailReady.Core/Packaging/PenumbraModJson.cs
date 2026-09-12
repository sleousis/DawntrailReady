// Ports: TexTools Mods/FileTypes/PMP.cs (LoadPMP, ValidateOption, IsEmptyOption, CanImport, PMPCombiningGroupJson)
// and Mods/WizardData.cs (FromPmp page ordering, FromPMPGroup empty-group handling).
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DawntrailReady.Core.Packaging;

internal enum PenumbraContainerKind { Default, Standard, Imc, Combining }

/// <summary>One Penumbra JSON file, kept as a mutable node tree so unknown fields and ordering survive a rewrite.</summary>
internal sealed class PenumbraJsonFile
{
    public required string FullPath { get; init; }
    public required JsonNode Root { get; init; }
    public bool Bom { get; init; }
    public bool Tabs { get; init; }
    public int IndentSize { get; init; } = 2;
    public string NewLine { get; init; } = "\n";
    public bool Dirty { get; set; }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static PenumbraJsonFile? Load(string fullPath)
    {
        var bytes = File.ReadAllBytes(fullPath);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        var root = JsonNode.Parse(text, null, DocumentOptions);
        if (root is null) return null;

        var tabs = text.Contains("\n\t");
        var indent = 2;
        var firstIndented = text.IndexOf("\n ", StringComparison.Ordinal);
        if (!tabs && firstIndented >= 0)
        {
            indent = 0;
            for (var i = firstIndented + 1; i < text.Length && text[i] == ' '; i++) indent++;
        }

        return new PenumbraJsonFile
        {
            FullPath = fullPath,
            Root = root,
            Bom = bom,
            Tabs = tabs,
            IndentSize = Math.Max(1, indent),
            NewLine = text.Contains("\r\n") ? "\r\n" : "\n",
        };
    }

    /// <summary>Serialises with the file's own indentation, newline and BOM.</summary>
    public byte[] ToBytes()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = Tabs ? '\t' : ' ',
            IndentSize = Tabs ? 1 : IndentSize,
            NewLine = NewLine,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var text = UnescapeText(Root.ToJsonString(options));
        var body = Encoding.UTF8.GetBytes(text);
        return Bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    /// <summary>
    /// System.Text.Json always writes characters outside the BMP (emoji) as <c>\uXXXX</c> pairs; Penumbra (Newtonsoft)
    /// writes them raw. Turns every <c>\uXXXX</c> escape back into its character, except control characters, quotes,
    /// backslashes and lone surrogates, which stay escaped.
    /// </summary>
    internal static string UnescapeText(string json)
    {
        if (!json.Contains("\\u", StringComparison.Ordinal)) return json;
        var sb = new StringBuilder(json.Length);
        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (c != '\\' || i + 1 >= json.Length)
            {
                sb.Append(c);
                continue;
            }

            if (json[i + 1] != 'u' || !TryHex(json, i + 2, out var u))
            {
                // Any other escape (\\, \", \n, ...) is copied whole so its second char is not re-read.
                sb.Append(c).Append(json[i + 1]);
                i++;
                continue;
            }

            if (char.IsHighSurrogate(u) && json.Length >= i + 12 && json[i + 6] == '\\' && json[i + 7] == 'u'
                && TryHex(json, i + 8, out var low) && char.IsLowSurrogate(low))
            {
                sb.Append(u).Append(low);
                i += 11;
            }
            else if (u < 0x20 || u == '"' || u == '\\' || char.IsSurrogate(u))
            {
                sb.Append(json, i, 6);
                i += 5;
            }
            else
            {
                sb.Append(u);
                i += 5;
            }
        }
        return sb.ToString();
    }

    private static bool TryHex(string s, int start, out char value)
    {
        value = '\0';
        if (start + 4 > s.Length) return false;
        if (!int.TryParse(s.AsSpan(start, 4), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var v)) return false;
        value = (char)v;
        return true;
    }
}

/// <summary>One option (or the default container) located in the mod's JSON.</summary>
internal sealed class PenumbraContainer
{
    public required string Key { get; init; }
    public required string GroupName { get; init; }
    public required string OptionName { get; init; }
    public required PenumbraContainerKind Kind { get; init; }
    public required JsonObject Option { get; init; }
    public required PenumbraJsonFile File { get; init; }

    public bool HasStandardData => Kind is PenumbraContainerKind.Default or PenumbraContainerKind.Standard;
}

/// <summary>
/// The JSON side of a Penumbra mod folder, in the option order TexTools' WizardData.FromPmp produces:
/// the default container (only if not empty), then groups ordered by Page (stable), then options in order.
/// </summary>
internal sealed class PenumbraModJson
{
    // PMPCombiningGroupJson.MaxCombiningOptions
    private const int MaxCombiningOptions = 8;

    // XivDataFile folder keys (General/Enums/XivDataFile.cs). Every expansion key starts with one of these.
    private static readonly string[] DataFolderKeys =
        ["common/", "bgcommon/", "bg/", "cut/", "chara/", "shader/", "ui/", "sound/", "vfx/", "exd/", "music/"];

    public required string ModFolder { get; init; }
    public int FileVersion { get; init; }
    public string Name { get; init; } = "";
    public List<PenumbraJsonFile> Files { get; } = [];
    public List<PenumbraContainer> Containers { get; } = [];

    /// <summary>PMP.CanImport: only paths inside a known game data folder are imported.</summary>
    public static bool CanImport(string internalFilePath)
    {
        foreach (var key in DataFolderKeys)
        {
            if (internalFilePath.StartsWith(key, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    public static PenumbraModJson Load(string modFolder)
    {
        var metaPath = Path.Combine(modFolder, "meta.json");
        var meta = PenumbraJsonFile.Load(metaPath) ?? throw new InvalidDataException("meta.json is empty.");
        var metaObj = meta.Root as JsonObject ?? throw new InvalidDataException("meta.json is not an object.");

        var json = new PenumbraModJson
        {
            ModFolder = modFolder,
            FileVersion = Int(metaObj["FileVersion"]) ?? 0,
            Name = Str(metaObj["Name"]),
        };
        json.Files.Add(meta);

        JsonObject? defaultData;
        PenumbraJsonFile defaultFile;
        var groups = new List<(JsonObject Group, PenumbraJsonFile File, string KeyPrefix)>();

        var metaGroups = metaObj["Groups"] as JsonArray;
        if ((metaGroups is { Count: > 0 }) || metaObj["DefaultData"] is not null)
        {
            // v4: Penumbra keeps everything in meta.json. TexTools pulls it back to v3 shape.
            defaultData = metaObj["DefaultData"] as JsonObject;
            defaultFile = meta;
            if (metaGroups is not null)
            {
                for (var gi = 0; gi < metaGroups.Count; gi++)
                {
                    if (metaGroups[gi] is JsonObject g) groups.Add((g, meta, $"group:{gi}"));
                }
            }
        }
        else
        {
            // v3: default_mod.json + group_*.json, in Directory.GetFiles order like LoadPMP.
            defaultData = null;
            defaultFile = meta;
            var defPath = Path.Combine(modFolder, "default_mod.json");
            if (File.Exists(defPath))
            {
                var def = PenumbraJsonFile.Load(defPath);
                if (def is not null)
                {
                    json.Files.Add(def);
                    defaultData = def.Root as JsonObject;
                    defaultFile = def;
                }
            }

            foreach (var file in Directory.GetFiles(modFolder))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith("group_", StringComparison.Ordinal) || !name.ToLowerInvariant().EndsWith(".json", StringComparison.Ordinal)) continue;
                var gf = PenumbraJsonFile.Load(file);
                if (gf?.Root is not JsonObject g) continue;
                json.Files.Add(gf);
                groups.Add((g, gf, $"group:{name}"));
            }
        }

        if (defaultData is not null && !IsEmptyOption(defaultData))
        {
            json.Containers.Add(new PenumbraContainer
            {
                Key = "default", GroupName = "Default", OptionName = "Default",
                Kind = PenumbraContainerKind.Default, Option = defaultData, File = defaultFile,
            });
        }

        // FromPmp creates pages 0..max(Page) and appends each group to its page: a stable sort by Page.
        var ordered = groups.Select((g, i) => (g, i, Page: Int(g.Group["Page"]) ?? 0)).ToList();
        if (ordered.Any(x => x.Page < 0)) throw new InvalidDataException("PMP group has a negative page index.");
        foreach (var (g, _, _) in ordered.OrderBy(x => x.Page).ThenBy(x => x.i))
        {
            var type = Str(g.Group["Type"]);
            var kind = type switch
            {
                "Single" or "Multi" => PenumbraContainerKind.Standard,
                "Imc" => PenumbraContainerKind.Imc,
                "Combining" => PenumbraContainerKind.Combining,
                _ => throw new InvalidDataException($"Unimplemented PMP group type: {type}"),
            };

            var options = (g.Group["Options"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
            if (kind == PenumbraContainerKind.Combining && options.Count > MaxCombiningOptions)
            {
                options = options.Take(MaxCombiningOptions).ToList();
            }

            // FromPMPGroup returns null for a group without options; ClearNulls drops it.
            for (var oi = 0; oi < options.Count; oi++)
            {
                json.Containers.Add(new PenumbraContainer
                {
                    Key = $"{g.KeyPrefix}/option:{oi}",
                    GroupName = Str(g.Group["Name"]),
                    OptionName = Str(options[oi]["Name"]),
                    Kind = kind,
                    Option = options[oi],
                    File = g.File,
                });
            }
        }

        return json;
    }

    /// <summary>PmpStandardOptionJson.IsEmptyOption.</summary>
    public static bool IsEmptyOption(JsonObject option) =>
        Count(option["Files"]) == 0 && Count(option["FileSwaps"]) == 0 && Count(option["Manipulations"]) == 0;

    /// <summary>The option's Files as (game path, relative path) in JSON order. Null/missing = empty (ValidateOption).</summary>
    public static List<(string GamePath, string RelativePath)> FileEntries(JsonObject option)
    {
        var ret = new List<(string, string)>();
        if (option["Files"] is not JsonObject files) return ret;
        foreach (var kv in files)
        {
            if (kv.Value is not JsonValue v || !v.TryGetValue<string>(out var rel))
                throw new InvalidDataException($"Invalid file path for {kv.Key} in mod JSON.");
            ret.Add((kv.Key, rel));
        }
        return ret;
    }

    public static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public static int? Int(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out i)) return i;
        return null;
    }

    private static int Count(JsonNode? node) => node switch
    {
        JsonObject o => o.Count,
        JsonArray a => a.Count,
        _ => 0,
    };
}
