# Changelog

Each release's section is shown in Dalamud's plugin installer and on the GitHub release page. The release
workflow refuses a tag whose version has no section here.

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
