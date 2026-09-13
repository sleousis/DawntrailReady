<div align="center">

<img src="src/DawntrailReady/images/icon.png" alt="Dawntrail Ready" width="128">

# Dawntrail Ready

**Quietly updates old Penumbra mods to Dawntrail when you import them.**

A Dalamud plugin for Final Fantasy XIV that brings mods made before Dawntrail (7.0) up to date the same way TexTools does, without TexTools. Import a mod in Penumbra as usual and it just works.

[![Latest release](https://img.shields.io/github/v/release/sleousis/DawntrailReady?label=release)](https://github.com/sleousis/DawntrailReady/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/sleousis/DawntrailReady/total?label=downloads)](https://github.com/sleousis/DawntrailReady/releases)
[![License](https://img.shields.io/github/license/sleousis/DawntrailReady)](LICENSE)

</div>

## Key features

- **Updates mods as you import them.** When Penumbra finishes importing a mod made before Dawntrail, Dawntrail Ready updates it in the background and Penumbra reloads it. No window opens and nothing asks you anything.
- **The same result as TexTools.** The conversion is TexTools' own Dawntrail upgrade, ported: materials and their colour tables, models, index maps, masks, hair, skin texture paths and old iris masks.
- **Leaves Dawntrail mods alone.** Only a mod that still holds files the game no longer reads is touched. A mod that already works in Dawntrail is never changed.
- **Scan your whole library.** See which of your mods were made before Dawntrail, tick the ones you want and update them all at once. Each one is reloaded in Penumbra right after.
- **Convert a mod file.** Pick a `.pmp` or `.ttmp2` and the updated copy is saved next to it as `(Dawntrail).pmp` and imported into Penumbra. The original file is never changed.
- **Light on your PC.** Nothing runs while you play. Updates happen on one low-priority background thread, and a mod whose update would create a texture larger than 4096×4096 is left untouched rather than loading the graphics card with it.

## Requirements

- **[Penumbra](https://github.com/xivdev/Penumbra)** loads your mods. Dawntrail Ready works through it and does nothing without it.

## Installation

1. Install [XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) and start the game through it with Dalamud enabled.
2. In game, type `/xlsettings` and open the **Experimental** tab.
3. Under **Custom Plugin Repositories**, paste the link below into the empty field, press the **+** button, then **Save and Close**.

   ```
   https://raw.githubusercontent.com/sleousis/DawntrailReady/main/repo.json
   ```

4. Type `/xlplugins`, search for **Dawntrail Ready** under **All Plugins** and install it.

Please install Dawntrail Ready this way rather than from a release zip. Dalamud then keeps it up to date for you.

## Commands

| Command | What it does |
| --- | --- |
| `/dtready` | Opens or closes the window |
| `/dtready scan` | Checks your Penumbra library for mods made before Dawntrail |
| `/dtready convert <file>` | Updates a `.pmp` or `.ttmp2` by its full path, like **Convert a mod file...** in the window |

Automatic updating on import can be switched off in the window.

## Before you use it

- **Files are changed in place and no backup is kept.** Keep the original download of any mod you care about, so you can reinstall it if you don't like the result.
- **Where the result differs from TexTools:**
  - A texture whose sides aren't a power of two is resized without TexTools' extra compression step, so it comes out slightly cleaner.
  - Very old TexTools packs get the standard Dawntrail model update, but not the full model rebuild TexTools also does for them.
  - A mod whose update would create a texture larger than 4096×4096 is left untouched.
- **Mods TexTools can't update** (for example eye mods with oversized masks) are left as they are, and the window says why.

## Support

Something not working? [Open an issue](https://github.com/sleousis/DawntrailReady/issues/new) with the mod's name, what you did and what happened. The lines under `/xllog` filtered on **DawntrailReady** help a lot.

## Contributing

Contributions are welcome. Please open an issue before writing any code, so we can agree on the change first.

Dawntrail Ready builds with the .NET 10 SDK against Dalamud API 15. The conversion lives in `DawntrailReady.Core`, which does not depend on the game, and its tests run with `dotnet test`.

## License

Dawntrail Ready is released under the [GNU General Public License v3.0](LICENSE). The conversion is ported from [TexTools](https://github.com/TexTools/xivModdingFramework)' xivModdingFramework, which is also GPL-3.0.
