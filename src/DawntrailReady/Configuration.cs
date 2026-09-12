using Dalamud.Configuration;

namespace DawntrailReady;

/// <summary>
/// Saved by Dalamud as JSON with Newtonsoft, every object stamped with its type name, under the plugin's
/// InternalName. Loading fills existing objects in place, and every public property is written (see the
/// dalamud:config skill before adding collections or computed properties).
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Bump when <see cref="Migrate"/> gains a step. New configs start here and skip the chain.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Update mods made before Dawntrail as soon as Penumbra imports them.</summary>
    public bool AutoConvertOnImport { get; set; } = true;

    /// <summary>Brings a config written by an older build up to date. Returns whether anything changed.</summary>
    public bool Migrate()
    {
        var changed = false;
        if (Version > CurrentVersion)
        {
            // Written by a newer build: leave the values, but don't claim a version this build doesn't know.
            Version = CurrentVersion;
            changed = true;
        }
        return changed;
    }

    public void Save(Dalamud.Plugin.IDalamudPluginInterface pi) => pi.SavePluginConfig(this);
}
