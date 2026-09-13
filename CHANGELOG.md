# Changelog

Each release's section is shown in Dalamud's plugin installer and on the GitHub release page. The release
workflow refuses a tag whose version has no section here.

## 0.1.4

- A new logo: the sunrise and update arrow, redrawn to the same rules as Gleam's and Soundswap's so the three plugins look like one family. Nothing else changes.

## 0.1.3

- Safer on your PC: a mod whose update would create a texture larger than 4096x4096 (possible with very large old eye masks) is now left untouched, and the reason is shown, instead of producing a texture that can overload the graphics card.
- Converting no longer uses every CPU core: all work stays on one low-priority background thread, so the game and other plugins keep running smoothly. Results are the same.
- An unexpected error in the background worker or in a /dtready command can no longer take the game down with it.

## 0.1.2

- New: "/dtready convert <file>" updates a .pmp or .ttmp2 named by its full path, the same way "Convert a mod file..." does.

## 0.1.1

- Eye mods that already ship Dawntrail iris textures are no longer treated as outdated because they also keep old iris masks; their own textures are left alone. The same goes for hair, tail and ear textures a mod already has in their Dawntrail form.
- Every file is written all the way to disk before it replaces the old one, so a system crash can no longer leave empty files behind.

## 0.1.0

- Mods you import in Penumbra are checked in the background. One made before Dawntrail is updated the way TexTools' Dawntrail upgrade does it (materials, models, index maps, masks, hair, skin, iris and hair texture paths) and Penumbra reloads it. Mods that already work in Dawntrail are left alone.
- "Scan my mods" lists the pre-Dawntrail mods in your Penumbra library; "Update" converts the ones you tick and reloads each in Penumbra.
- "Convert a mod file..." updates a .pmp or .ttmp2, saves it next to the original as "(Dawntrail).pmp" and imports it into Penumbra.
- No backups are kept: files are changed in place.
