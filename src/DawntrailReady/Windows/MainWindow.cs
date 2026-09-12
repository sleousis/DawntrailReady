using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using DawntrailReady.Core.Service;

namespace DawntrailReady.Windows;

/// <summary>What the window can ask the plugin to do.</summary>
public interface IMainWindowActions
{
    bool PenumbraReady { get; }
    ServiceSnapshot Snapshot { get; }
    void StartScan();
    void UpdateMods(IReadOnlyList<ScanHit> hits);
    void PickModFile();
}

/// <summary>
/// The only window. It never opens by itself: the plugin works without it, and this is where a player looks
/// when they want to scan their library, convert a file, or see what happened.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly Vector4 Muted = new(0.62f, 0.64f, 0.68f, 1f);
    private static readonly Vector4 Ok = new(0.45f, 0.78f, 0.55f, 1f);
    private static readonly Vector4 Bad = new(0.93f, 0.45f, 0.42f, 1f);

    private readonly Configuration config;
    private readonly Action save;
    private readonly IMainWindowActions actions;

    // Ticks for the current scan result, by mod folder. A new result starts with everything ticked.
    private readonly Dictionary<string, bool> ticks = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? ticksFor;

    public MainWindow(Configuration config, Action save, IMainWindowActions actions) : base("Dawntrail Ready###DawntrailReadyMain")
    {
        this.config = config;
        this.save = save;
        this.actions = actions;
        Size = new Vector2(520, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 300), MaximumSize = new Vector2(4000, 3000) };
    }

    public override void Draw()
    {
        var snap = actions.Snapshot;
        var ready = actions.PenumbraReady;

        if (!ready)
            ImGui.TextColored(Bad, "Penumbra isn't loaded. Dawntrail Ready waits for it.");

        var auto = config.AutoConvertOnImport;
        if (ImGui.Checkbox("Automatically update mods I import", ref auto))
        {
            config.AutoConvertOnImport = auto;
            save();
        }
        Hint("Mods made before Dawntrail are updated the moment Penumbra imports them, the same way TexTools would.");

        Section("Your mods");
        DrawLibrary(snap, ready);

        Section("A mod file");
        using (ImRaii.Disabled(snap.Busy))
            if (ImGui.Button("Convert a mod file..."))
                actions.PickModFile();
        Hint("Choose a .pmp or .ttmp2. The updated copy is saved next to it as \"(Dawntrail).pmp\" and imported into Penumbra.");

        if (snap.Busy && snap.Status.Length > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Muted, snap.Status + "...");
        }

        Section("Recent");
        DrawRecent(snap.Recent);
    }

    private void DrawLibrary(ServiceSnapshot snap, bool ready)
    {
        var scan = snap.Scan;
        if (scan is { Running: true })
        {
            var fraction = scan.Total == 0 ? 0f : (float)scan.Checked / scan.Total;
            ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"Checked {scan.Checked} of {scan.Total}");
            return;
        }

        using (ImRaii.Disabled(!ready || snap.Busy))
            if (ImGui.Button("Scan my mods"))
                actions.StartScan();

        if (scan is null)
        {
            Hint("Looks through every mod Penumbra has and lists the ones made before Dawntrail.");
            return;
        }

        if (ticksFor != scan.FinishedAt)
        {
            ticks.Clear();
            foreach (var hit in scan.Hits) ticks[hit.ModDirectory] = true;
            ticksFor = scan.FinishedAt;
        }

        if (scan.Hits.Count == 0)
        {
            ImGui.TextColored(Ok, $"All {scan.Checked} mods are Dawntrail-ready.");
            if (scan.Failed > 0) ImGui.TextColored(Muted, $"{scan.Failed} couldn't be checked or updated; /xllog says why.");
            return;
        }

        ImGui.TextUnformatted($"{scan.Hits.Count} of {scan.Checked} mods were made before Dawntrail:");
        var rowHeight = ImGui.GetTextLineHeightWithSpacing();
        var height = Math.Min(scan.Hits.Count, 8) * rowHeight + ImGui.GetStyle().WindowPadding.Y * 2;
        using (var child = ImRaii.Child("##hits", new Vector2(-1, height), true))
        {
            if (child)
            {
                foreach (var hit in scan.Hits)
                {
                    var on = ticks.GetValueOrDefault(hit.ModDirectory);
                    if (ImGui.Checkbox($"{hit.ModName}##{hit.ModDirectory}", ref on)) ticks[hit.ModDirectory] = on;
                    var what = Describe(hit);
                    if (what.Length > 0)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(Muted, what);
                    }
                }
            }
        }

        var selected = scan.Hits.Where(h => ticks.GetValueOrDefault(h.ModDirectory)).ToList();
        using (ImRaii.Disabled(selected.Count == 0 || snap.Busy || !ready))
            if (ImGui.Button(selected.Count == 1 ? "Update 1 mod" : $"Update {selected.Count} mods"))
                actions.UpdateMods(selected);
        if (snap.Total > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"{snap.Done} of {snap.Total} done");
        }
    }

    private static string Describe(ScanHit hit)
    {
        var parts = new List<string>();
        if (hit.Materials > 0) parts.Add(hit.Materials == 1 ? "1 material" : $"{hit.Materials} materials");
        if (hit.Models > 0) parts.Add(hit.Models == 1 ? "1 model" : $"{hit.Models} models");
        return parts.Count == 0 ? "(textures)" : $"({string.Join(", ", parts)})";
    }

    private static void DrawRecent(IReadOnlyList<ActivityEntry> recent)
    {
        if (recent.Count == 0)
        {
            Hint("Updates and conversions will be listed here.");
            return;
        }
        foreach (var e in recent.Reverse().Take(8))
        {
            ImGui.TextColored(Muted, e.At.ToLocalTime().ToString("HH:mm"));
            ImGui.SameLine();
            var text = e.Mod.Length > 0 ? $"{e.Mod}: {e.Detail}" : e.Detail;
            if (e.Failed) ImGui.TextColored(Bad, text);
            else ImGui.TextWrapped(text);
        }
    }

    private static void Section(string title)
    {
        ImGui.Dummy(new Vector2(0, 6 * ImGuiHelpers.GlobalScale));
        ImGui.TextUnformatted(title);
        ImGui.Separator();
    }

    private static void Hint(string text)
    {
        using var color = ImRaii.PushColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped(text);
    }
}
